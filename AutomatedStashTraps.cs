using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Libraries;
using ProtoBuf;
using Rust;
using Rust.Workshop;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections;

namespace Oxide.Plugins
{
    [Info("Automated Stash Traps", "Dana", "2.0.0")]
    [Description("Spawns fully automated stash traps across the map to catch ESP cheaters.")]
    public class AutomatedStashTraps : RustPlugin
    {
        #region Fields

        private static AutomatedStashTraps instance;
        private static Configuration config;
        private static Data data;

        private SpawnPointManager spawnPointManager;
        private SkinManager skinManager;

        // Coroutine reference for spawning automated traps.
        private Coroutine spawnCoroutine;

        // List of players who are manually deploying traps.
        private List<BasePlayer> manualTrapDeployers = new List<BasePlayer>();
        // Set of player-owned stashes that have been revealed.
        private HashSet<uint> revealedOwnedStashes = new HashSet<uint>();
        // Dictionary of players who are currently editing the stash loot table.
        private Dictionary<BasePlayer, StorageContainer> activeLootEditors = new Dictionary<BasePlayer, StorageContainer>();

        // Prefab paths.
        private const string blueprintTemplate = "blueprintbase";
        private const string stashPrefab = "assets/prefabs/deployable/small stash/small_stash_deployed.prefab";
        private const string storagePrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
        private const string sleepingBagPrefab = "assets/prefabs/deployable/sleeping bag/sleepingbag_leather_deployed.prefab";

        // Last known position of a revealed stash.
        private Vector3 lastRevealedStashPosition;

        #endregion

        #region Configuration

        private class Configuration
        {
            [JsonProperty(PropertyName = "Version")]
            public VersionNumber Version { get; set; }

            [JsonProperty(PropertyName = "Spawn Point")]
            public SpawnPointOptions SpawnPoint { get; set; }

            [JsonProperty(PropertyName = "Automated Trap")]
            public AutomatedTrapOptions AutomatedTrap { get; set; }

            [JsonProperty(PropertyName = "Moderation")]
            public ModerationOptions Moderation { get; set; }

            [JsonProperty(PropertyName = "Discord")]
            public DiscordOptions Discord { get; set; }

            [JsonProperty(PropertyName = "Stash Loot")]
            public StashLootOptions StashLoot { get; set; }
        }

        private class SpawnPointOptions
        {
            [JsonProperty(PropertyName = "Maximum Attempts To Find Spawn Points")]
            public int MaximumAttemptsToFindSpawnPoints { get; set; }

            [JsonProperty(PropertyName = "Position Scan Radius")]
            public float PositionScanRadius { get; set; }

            [JsonProperty(PropertyName = "Entity Detection Radius")]
            public float EntityDetectionRadius { get; set; }

            [JsonProperty(PropertyName = "Player Detection Radius")]
            public float PlayerDetectionRadius { get; set; }
        }

        private class AutomatedTrapOptions
        {
            [JsonProperty(PropertyName = "Maximum Traps To Spawn")]
            public int MaximumTrapsToSpawn { get; set; }

            [JsonProperty(PropertyName = "Destroy Revealed Trap After Minutes")]
            public int DestroyRevealedTrapAfterMinutes { get; set; }

            [JsonProperty(PropertyName = "Replace Revealed Trap")]
            public bool ReplaceRevealedTrap { get; set; }

            [JsonProperty(PropertyName = "Dummy Sleeping Bag")]
            public DummySleepingBagOptions DummySleepingBag { get; set; }
        }

        private class DummySleepingBagOptions
        {
            [JsonProperty(PropertyName = "Spawn Along")]
            public bool SpawnAlong { get; set; }

            [JsonProperty(PropertyName = "Spawn Chance")]
            public int SpawnChance { get; set; }

            [JsonProperty(PropertyName = "Randomized Skin Chance")]
            public int RandomizedSkinChance { get; set; }
            
            [JsonProperty(PropertyName = "Randomized Nice Name Chance")]
            public int RandomizedNiceNameChance { get; set; }
        }
        
        private class ModerationOptions
        {
            [JsonProperty(PropertyName = "Automatic Ban")]
            public bool AutomaticBan { get; set; }

            [JsonProperty(PropertyName = "Revealed Traps Tolerance")]
            public int RevealedTrapsTolerance { get; set; }

            [JsonProperty(PropertyName = "Ban Delay Seconds")]
            public int BanDelaySeconds { get; set; }

            [JsonProperty(PropertyName = "Ban Reason")]
            public string BanReason { get; set; }
        }

        private class DiscordOptions
        {
            [JsonProperty(PropertyName = "Post Into Discord")]
            public bool PostIntoDiscord { get; set; }

            [JsonProperty(PropertyName = "Webhook Url")]
            public string WebhookUrl { get; set; }

            [JsonProperty(PropertyName = "Embed Color")]
            public string EmbedColor { get; set; }
        }

        private class StashLootOptions
        {
            [JsonProperty(PropertyName = "Minimum Loot Spawn Slots")]
            public int MinimumLootSpawnSlots { get; set; }

            [JsonProperty(PropertyName = "Maximum Loot Spawn Slots")]
            public int MaximumLootSpawnSlots { get; set; }

            [JsonProperty(PropertyName = "Spawn Chance As Blueprint")]
            public int SpawnChanceAsBlueprint { get; set; }

            [JsonProperty(PropertyName = "Spawn Chance With Skin")]
            public int SpawnChanceWithSkin { get; set; }

            [JsonProperty(PropertyName = "Spawn Chance As Damaged")]
            public int SpawnChanceAsDamaged { get; set; }

            [JsonProperty(PropertyName = "Minimum Condition Loss")]
            public float MinimumConditionLoss { get; set; }

            [JsonProperty(PropertyName = "Maximum Condition Loss")]
            public float MaximumConditionLoss { get; set; }

            [JsonProperty(PropertyName = "Spawn Chance As Repaired")]
            public int SpawnChanceAsRepaired { get; set; }

            [JsonProperty(PropertyName = "Spawn Chance As Broken")]
            public int SpawnChanceAsBroken { get; set; }

            [JsonProperty(PropertyName = "Loot Table")]
            public List<ItemInfo> LootTable { get; set; } = new List<ItemInfo>();
        }

        private class ItemInfo
        {
            [JsonProperty(PropertyName = "Short Name")]
            public string ShortName { get; set; }

            [JsonProperty(PropertyName = "Minimum Spawn Amount")]
            public int MinimumSpawnAmount { get; set; }

            [JsonProperty(PropertyName = "Maximum Spawn Amount")]
            public int MaximumSpawnAmount { get; set; }

            [JsonIgnore]
            private ItemDefinition itemDefinition;

            [JsonIgnore]
            private bool itemIsValidated;

            // Inspired by WhiteThunder's AutomatedWorkcarts plugin.
            /// <summary>
            /// Returns the item definition associated with this item.
            /// </summary>
            /// <returns> The item definition, or null if the item is not valid. </returns>
            public ItemDefinition GetItemDefinition()
            {
                if (!itemIsValidated)
                {
                    ItemDefinition lookupResult = ItemManager.FindItemDefinition(ShortName);
                    if (lookupResult != null)
                        itemDefinition = lookupResult;
                    else
                        return null; // Lang: Invalid item short name in config

                    itemIsValidated = true;
                }

                return itemDefinition;
            }

            /// <summary>
            /// Determines whether the item can be researched.
            /// </summary>
            /// <returns> True if the item can be researched, false otherwise. </returns>
            public bool CanBeResearched()
            {
                return itemDefinition.Blueprint == null || !itemDefinition.Blueprint.isResearchable ? false : true;
            }

            /// <summary>
            /// Determines whether the item has skins.
            /// </summary>
            /// <returns> True if the item can be skinned, false otherwise. </returns>
            public bool CanBeSkinned()
            {
                return !itemDefinition.HasSkins ? false : true;
            }

            /// <summary>
            /// Determines whether the item can be repaired.
            /// </summary>
            /// <returns> True if the item can be repaired, false otherwise. </returns>
            public bool CanBeRepaired()
            {
                return !itemDefinition.condition.repairable ? false : true; // !item.hasCondition
            }
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version,

                SpawnPoint = new SpawnPointOptions
                {
                    MaximumAttemptsToFindSpawnPoints = 1500,
                    PositionScanRadius = 3f,
                    EntityDetectionRadius = 25f,
                    PlayerDetectionRadius = 25f
                },

                AutomatedTrap = new AutomatedTrapOptions
                {
                    MaximumTrapsToSpawn = 50,
                    DestroyRevealedTrapAfterMinutes = 5,
                    ReplaceRevealedTrap = true,
                    DummySleepingBag = new DummySleepingBagOptions
                    {
                        SpawnAlong = true,
                        SpawnChance = 50,
                        RandomizedSkinChance = 0,
                        RandomizedNiceNameChance = 40
                    }
                },
                
                Moderation = new ModerationOptions
                {
                    AutomaticBan = false,
                    RevealedTrapsTolerance = 3,
                    BanDelaySeconds = 60,
                    BanReason = "Cheat Detected!"
                },

                Discord = new DiscordOptions
                {
                    PostIntoDiscord = false,
                    WebhookUrl = string.Empty,
                    EmbedColor = "#FFFFFF",
                },

                StashLoot = new StashLootOptions
                {
                    MinimumLootSpawnSlots = 1,
                    MaximumLootSpawnSlots = 6,
                    SpawnChanceAsBlueprint = 10,
                    SpawnChanceWithSkin = 50,
                    SpawnChanceAsDamaged = 30,
                    MinimumConditionLoss = 5f,
                    MaximumConditionLoss = 95f,
                    SpawnChanceAsRepaired = 15,
                    SpawnChanceAsBroken = 5,
                    LootTable = new List<ItemInfo>()
                    {
                        new ItemInfo
                        {
                            ShortName = "scrap",
                            MinimumSpawnAmount = 25,
                            MaximumSpawnAmount = 125,
                        },
                        new ItemInfo
                        {
                            ShortName = "metal.refined",
                            MinimumSpawnAmount = 15,
                            MaximumSpawnAmount = 40,
                        },
                        new ItemInfo
                        {
                            ShortName = "cloth",
                            MinimumSpawnAmount = 60,
                            MaximumSpawnAmount = 200,
                        },
                       new ItemInfo
                        {
                            ShortName = "cctv.camera",
                            MinimumSpawnAmount = 1,
                            MaximumSpawnAmount = 2,
                        },
                        new ItemInfo
                        {
                            ShortName = "riflebody",
                            MinimumSpawnAmount = 1,
                            MaximumSpawnAmount = 3,
                        },
                        new ItemInfo
                        {
                            ShortName = "techparts",
                            MinimumSpawnAmount = 1,
                            MaximumSpawnAmount = 6,
                        }
                    }
                }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();

            if (config.Version < Version)
                UpdateConfig();

            ValidateConfigValues();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Detected changes in configuration! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (config.Version < new VersionNumber(1, 0, 0))
                config = defaultConfig;

            PrintWarning("Configuration update complete! Updated from version " + config.Version + " to " + Version);
            config.Version = Version;
        }
        
        private void ValidateConfigValues()
        {
            PrintWarning("Validating configuration values...");

            if (config.AutomatedTrap.DestroyRevealedTrapAfterMinutes <= 0)
            {
                PrintWarning("Invalid trap removal time value. To avoid potential entity leaks, this value must be greater than 0. Default value of 5 will be applied.");
                config.AutomatedTrap.DestroyRevealedTrapAfterMinutes = 5;
            }

            if (config.StashLoot.MinimumLootSpawnSlots < 1)
            {
                PrintWarning("Invalid minimum loot spawn slots value. Default value of 1 will be applied.");
                config.StashLoot.MinimumLootSpawnSlots = 1;
            }

            if (config.StashLoot.MaximumLootSpawnSlots > 6)
            {
                PrintWarning("Invalid maximum loot spawn slots value. Default value of 6 will be applied.");
                config.StashLoot.MaximumLootSpawnSlots = 6;               
            }

            List<ItemInfo> invalidItems = config.StashLoot.LootTable.Where(item => item.GetItemDefinition() == null).ToList();
            foreach (ItemInfo invalidItem in invalidItems)
            {
                config.StashLoot.LootTable.Remove(invalidItem);
                PrintWarning("Invalid item '" + invalidItem.ShortName + "' removed from the loot table.");
            }

            foreach (ItemInfo item in config.StashLoot.LootTable)
            {
                if (item.MinimumSpawnAmount <= 0)
                {
                    PrintWarning("Invalid minimum spawn amount for item '" + item.ShortName + "'. Default value of 1 will be applied.");
                    item.MinimumSpawnAmount = 1;
                }

                if (item.MaximumSpawnAmount < item.MinimumSpawnAmount)
                {
                    PrintWarning("Invalid maximum spawn amount for item '" + item.ShortName + "'. Default value of " + item.MinimumSpawnAmount + " will be applied.");
                    item.MaximumSpawnAmount = item.MinimumSpawnAmount;
                }
            }

            PrintWarning("Configuration validation complete!");
        }

        #endregion

        #region Data

        private class Data
        {
            [JsonProperty(PropertyName = "Violations")]
            public Dictionary<ulong, int> Violations { get; set; } = new Dictionary<ulong, int>();

            [JsonProperty(PropertyName = "Automated Traps")]
            public Dictionary<uint, AutomatedTrapData> AutomatedTraps { get; set; } = new Dictionary<uint, AutomatedTrapData>();

            public static Data Load()
            {
                return Interface.Oxide.DataFileSystem.ReadObject<Data>(instance.Name) ?? new Data();
            }

            public Data Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(instance.Name, this);
                return this;
            }

            public static Data Clear()
            {
                return new Data().Save();
            }

            public void RemovePlayerData(BasePlayer player)
            {
                if (Violations.ContainsKey(player.userID))
                    Violations.Remove(player.userID);
            }

            public void CreateOrUpdatePlayerData(BasePlayer player)
            {
                int revealedTraps;

                Violations.TryGetValue(player.userID, out revealedTraps);
                Violations[player.userID] = revealedTraps + 1;
            }
            
            public int GetPlayerRevealedTrapsCount(BasePlayer player)
            {
                int revealedTraps;
                return Violations.TryGetValue(player.userID, out revealedTraps) ? revealedTraps : 0;
            }

            public void CreateTrapData(StashContainer stash, SleepingBag sleepingBag = null)
            {
                AutomatedTraps[stash.net.ID] = new AutomatedTrapData
                {
                    DummyStash = new DummyStashData
                    {
                        Hidden = true,
                        Id = stash.net.ID,
                        Position = stash.ServerPosition
                    }
                };

                if (sleepingBag != null)
                    AutomatedTraps[stash.net.ID].DummySleepingBag = new DummySleepingBagData
                    {
                        Id = sleepingBag.net.ID,
                        NiceName = sleepingBag.niceName,
                        SkinId = sleepingBag.skinID,
                        Position = sleepingBag.ServerPosition
                    };
            }

            public AutomatedTrapData GetTrapData(uint trapId)
            {
                AutomatedTrapData trapData;
                return AutomatedTraps.TryGetValue(trapId, out trapData) ? trapData : null;
            }

            public void UpdateTrapData(AutomatedTrapData trap)
            {
                trap.DummyStash.Hidden = false;
            }
        }

        private class AutomatedTrapData
        {
            [JsonProperty(PropertyName = "Dummy Stash")]
            public DummyStashData DummyStash { get; set; }

            [JsonProperty(PropertyName = "Dummy Sleeping Bag")]
            public DummySleepingBagData DummySleepingBag { get; set; }
        }

        private class DummyStashData
        {
            [JsonProperty(PropertyName = "Hidden")]
            public bool Hidden { get; set; }

            [JsonProperty(PropertyName = "Id")]
            public uint Id { get; set; }

            [JsonProperty(PropertyName = "Position")]
            public Vector3 Position { get; set; }
        }

        private class DummySleepingBagData
        {
            [JsonProperty(PropertyName = "Id")]
            public uint Id { get; set; }

            [JsonProperty(PropertyName = "Nice Name")]
            public string NiceName { get; set; }

            [JsonProperty(PropertyName = "Skin Id")]
            public ulong SkinId { get; set; }

            [JsonProperty(PropertyName = "Position")]
            public Vector3 Position { get; set; }
        }

        #endregion

        #region Oxide Hooks

        /// <summary>
        /// Hook: Called when a plugin is being initialized.
        /// </summary>
        private void Init()
        {
            instance = this;
            skinManager = new SkinManager();
            spawnPointManager = new SpawnPointManager();

            data = Data.Load();
            Permission.Register();
        }

        /// <summary>
        /// Hook: Called after server startup is complete and awaits connections or when a plugin is hotloaded while the server is running.
        /// </summary>
        private void OnServerInitialized()
        {
            StartSpawnCoroutine();
        }

        /// <summary>
        /// Hook: Called when a plugin is being unloaded.
        /// </summary>
        private void Unload()
        {
            CleanupTraps();
            StopSpawnCoroutine();
            spawnPointManager.ClearAvailableSpawnPoints();

            lastRevealedStashPosition = Vector3.zero;

            instance = null;
            config = null;
            data = null;
        }

        /// <summary>
        /// Hook: Called when any entity is built or deployed.
        /// </summary>
        /// <param name="planner"> The building planner held by the player. </param>
        /// <param name="gameObject"> Contains information about the built entity. </param>
        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            // Obtain the stash container entity from the game object component.
            StashContainer stash = gameObject?.ToBaseEntity() as StashContainer;
            if (!stash)
                return;

            // Obtain the deploying player from the planner.
            BasePlayer deployingPlayer = planner?.GetOwnerPlayer();
            if (!deployingPlayer)
                return;

            // Don't proceed if the deploying player is not on the list of players allowed to create manual traps.
            if (!manualTrapDeployers.Contains(deployingPlayer))
                return;

            // Initialize the stash by populating it with loot and hiding it underground.
            PopulateLoot(stash);
            stash.SetHidden(true);

            manualTrapDeployers.Remove(deployingPlayer);
        }

        /// <summary>
        /// Hook: Called when the player stops looting.
        /// </summary>
        /// <param name="inventory"> The inventory that the player was looting. </param>
        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            CloseLootEditor(inventory);
        }

        /// <summary>
        /// Hook: Called when an entity is destroyed.
        /// </summary>
        /// <param name="stash"> The stash container that has been destroyed. </param>
        private void OnEntityKill(StashContainer stash)
        {
            if (stash.IsValid())
                HandleDestroyedStash(stash);
        }

        /// <summary>
        /// Hook: Called when a player reveals a hidden stash.
        /// </summary>
        /// <param name="stash"> The stash that was revealed. </param>
        /// <param name="player"> The player who revealed the stash. </param>
        private void OnStashExposed(StashContainer stash, BasePlayer player)
        {
            OnStashTriggered(stash, player, stashWasDestroyed: false);
        }

        #endregion

        #region Spawn Coroutine

        /// <summary>
        /// Starts a coroutine that gradually spawns automated traps over time.
        /// </summary>
        private void StartSpawnCoroutine()
        {
            // Hold a reference to the coroutine that is currently running.
            spawnCoroutine = ServerMgr.Instance.StartCoroutine(SpawnTraps());
        }

        /// <summary>
        /// Stops the periodic spawning of automated traps if it is currently running.
        /// </summary>
        private void StopSpawnCoroutine()
        {
            // Proceed if the coroutine is currently running.
            if (!spawnCoroutine.IsUnityNull())
            {
                // Stop the execution of the coroutine.
                ServerMgr.Instance.StopCoroutine(spawnCoroutine);
                // Release the coroutine reference to allow it to be garbage collected.
                spawnCoroutine = null;
            }
        }

        #endregion Spawn Coroutine

        #region Traps Creation

        /// <summary>
        /// Spawns a specified number of automated traps, consisting of a stash and, optionally, a dummy sleeping bag.
        /// </summary>
        /// <returns> The number of traps that were spawned. </returns>
        private IEnumerator SpawnTraps()
        {
            // Keep track of the number of traps that have been spawned.
            int spawnedTraps = 0;
            // Calculate the number of traps that need to be spawned.
            int trapsToSpawn = config.AutomatedTrap.MaximumTrapsToSpawn - data.AutomatedTraps.Where(trapData => trapData.Value.DummyStash.Hidden).Count();
            // Determine the wait duration for the coroutine based on the current frame rate limit.
            WaitForSeconds waitDuration = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(0.01f) : null;

            // If there are not enough available spawn points, generate more until there are enough.
            if (spawnPointManager.AvailableSpawnPointsCount < trapsToSpawn)
            {
                int spawnPointsToGenerate = trapsToSpawn - spawnPointManager.AvailableSpawnPointsCount;
                yield return ServerMgr.Instance.StartCoroutine(spawnPointManager.GenerateSpawnPoints(spawnPointsToGenerate));
            }

            // Begin spawning traps until the required number has been reached.
            for (int i = 0; i < trapsToSpawn; i++)
            {
                // Get a random spawn point.
                Tuple<Vector3, Quaternion> spawnPoint = spawnPointManager.GetRandomSpawnPoint();

                // Create a stash container entity at the spawn point and populate it with loot.
                StashContainer stash = CreateStashEntity(stashPrefab, spawnPoint.Item1, spawnPoint.Item2);
                PopulateLoot(stash);

                // Initialize a sleeping bag entity, which may be spawned if the configuration allows it.
                SleepingBag sleepingBag = null;
                if (config.AutomatedTrap.DummySleepingBag.SpawnAlong && ChanceSucceeded(config.AutomatedTrap.DummySleepingBag.SpawnChance))
                {
                    // Find a nearby spawn point and create a sleeping bag at it.
                    Tuple<Vector3, Quaternion> nearbySpawnPoint = spawnPointManager.FindChildSpawnPoint(spawnPoint.Item1);
                    sleepingBag = CreateSleepingBagEntity(sleepingBagPrefab, nearbySpawnPoint.Item1, nearbySpawnPoint.Item2);
                }

                data.CreateTrapData(stash, sleepingBag);
                spawnedTraps++;

                // Wait for a set duration to prevent overloading the server with spawning actions.
                yield return waitDuration;
            }

            // Output the total number of spawned traps to the console.
            Puts("Spawned " + spawnedTraps + " traps.");
            // Save the trap data and set the coroutine to null to be garbage collected.
            data.Save();
            spawnCoroutine = null;
        }

        /// <summary>
        /// Creates a stash entity from the specified prefab at the given position and rotation.
        /// </summary>
        /// <param name="prefabPath"> The path to the prefab to use for the stash entity. </param>
        /// <param name="position"> The position to spawn the stash entity at. </param>
        /// <param name="rotation"> The rotation to spawn the stash entity with. </param>
        /// <returns> The created stash entity, or null if the entity could not be created. </returns>
        private StashContainer CreateStashEntity(string prefabPath, Vector3 position, Quaternion rotation)
        {
            // Create the entity from the specified prefab.
            BaseEntity entity = GameManager.server.CreateEntity(prefabPath, position, rotation);
            // Don't proceed if the entity could not be created.
            if (entity == null)
                return null;

            // Convert the entity to a StashContainer.
            StashContainer stash = entity as StashContainer;
            if (stash == null)
            {
                // Destroy the entity if it could not be converted.
                UnityEngine.Object.Destroy(entity);
                return null;
            }

            // Initialize the stash by spawning and hiding it underground.
            stash.Spawn();
            stash.SetHidden(true);
            // Cancel the decay invoke, so the stash does not decay over time.
            stash.CancelInvoke(stash.Decay);

            return stash;
        }

        /// <summary>
        /// Creates a sleeping bag entity from the specified prefab at the given position and rotation.
        /// </summary>
        /// <param name="prefabPath"> The path to the prefab to use for the sleeping bag entity. </param>
        /// <param name="position"> The position to spawn the sleeping bag entity at. </param>
        /// <param name="rotation"> The rotation to spawn the sleeping bag entity with. </param>
        /// <returns> The created sleeping bag entity, or null if the entity could not be created. </returns>
        private SleepingBag CreateSleepingBagEntity(string prefabPath, Vector3 position, Quaternion rotation)
        {
            // Create the entity from the specified prefab.
            BaseEntity entity = GameManager.server.CreateEntity(prefabPath, position, rotation);
            // Don't proceed if the entity could not be created.
            if (entity == null)
                return null;

            // Convert the entity to a SleepingBag.
            SleepingBag sleepingBag = entity as SleepingBag;
            if (sleepingBag == null)
            {
                // Destroy the entity if it could not be converted.
                UnityEngine.Object.Destroy(entity);
                return null;
            }

            // Set a random skin for the sleeping bag.
            if (config.AutomatedTrap.DummySleepingBag.RandomizedSkinChance > 0 && ChanceSucceeded(config.AutomatedTrap.DummySleepingBag.RandomizedSkinChance))
                sleepingBag.skinID = skinManager.GetSkinsForItem(ItemManager.FindItemDefinition("sleepingbag")).GetRandom();

            // Set a random nice name for the sleeping bag.
            if (config.AutomatedTrap.DummySleepingBag.RandomizedNiceNameChance > 0 && ChanceSucceeded(config.AutomatedTrap.DummySleepingBag.RandomizedNiceNameChance))
                sleepingBag.niceName = RandomUsernames.Get(Random.Range(0, 5000));

            // Spawn the sleeping bag.
            sleepingBag.Spawn();
            return sleepingBag;
        }

        private void PopulateLoot(StashContainer stash)
        {
            List<ItemInfo> itemsToSpawn = new List<ItemInfo>(config.StashLoot.LootTable);
            int lootSpawnSlots = Random.Range(config.StashLoot.MinimumLootSpawnSlots, config.StashLoot.MaximumLootSpawnSlots);

            if (lootSpawnSlots > itemsToSpawn.Count)
                lootSpawnSlots = itemsToSpawn.Count;

            stash.inventory.Clear();

            for (int i = 0; i < lootSpawnSlots; i++)
            {
                Item item;
                ItemInfo randomItem = itemsToSpawn.GetRandom();
                ItemDefinition itemDefinition = randomItem.GetItemDefinition();

                if (itemDefinition == null)
                    continue;

                if (config.StashLoot.SpawnChanceAsBlueprint > 0 && randomItem.CanBeResearched() && ChanceSucceeded(config.StashLoot.SpawnChanceAsBlueprint))
                {
                    item = ItemManager.CreateByName(blueprintTemplate);
                    item.blueprintTarget = itemDefinition.itemid;
                }
                else
                {
                    int spawnAmount = Random.Range(randomItem.MinimumSpawnAmount, randomItem.MaximumSpawnAmount + 1);
                    ulong skin = 0;

                    if (config.StashLoot.SpawnChanceWithSkin > 0 && randomItem.CanBeSkinned() && ChanceSucceeded(config.StashLoot.SpawnChanceWithSkin))
                        skin = instance.skinManager.GetSkinsForItem(itemDefinition).GetRandom();

                    item = ItemManager.CreateByName(randomItem.ShortName, spawnAmount, skin);

                    if (config.StashLoot.SpawnChanceAsDamaged > 0 && randomItem.CanBeRepaired())
                        RandomizeItemCondition(item);
                }

                // Remove the item if it wasn't added successfully to avoid any potential entities leak.
                if (!item.MoveToContainer(stash.inventory))
                    item.Remove();

                item.MarkDirty();
                itemsToSpawn.Remove(randomItem);
            }

            Pool.FreeList(ref itemsToSpawn);
        }

        private void RandomizeItemCondition(Item item)
        {
            if (ChanceSucceeded(config.StashLoot.SpawnChanceAsDamaged))
            {
                float conditionLoss = Random.Range(config.StashLoot.MinimumConditionLoss, config.StashLoot.MaximumConditionLoss);
                item.conditionNormalized = conditionLoss / 100;
            }

            if (ChanceSucceeded(config.StashLoot.SpawnChanceAsRepaired))
            {
                float repairAmount = Random.Range(1f, 0.8f);
                item.DoRepair(repairAmount);
            }
            else if (ChanceSucceeded(config.StashLoot.SpawnChanceAsBroken))
            {
                item.condition = 0f;
            }
        }

        #endregion Traps Creation

        #region Traps Removal

        /// <summary>
        /// Removes all automated traps from the world and their associated entities.
        /// </summary>
        /// <returns> The number of removed traps. </returns>
        private void CleanupTraps()
        {
            // Keep track of the number of removed traps.
            int removedTraps = 0;
            // Process all traps one by one.
            foreach (uint trapId in data.AutomatedTraps.Keys)
            {
                // Retrieve the data for the current trap.
                AutomatedTrapData trap = data.GetTrapData(trapId);
                // Skip the trap if its data cannot be found and move on to the next one.
                if (trap == null)
                    continue;

                // Find the stash for the current trap and kill it if found.
                StashContainer stash = FindEntityById(trap.DummyStash.Id) as StashContainer;
                stash?.Kill();

                // Find the dummy sleeping bag associated with the trap and kill it if found.
                if (trap.DummySleepingBag != null)
                {
                    SleepingBag sleepingBag = FindEntityById(trap.DummySleepingBag.Id) as SleepingBag;
                    sleepingBag?.Kill();
                }

                // Increment the number of successfully removed traps.
                removedTraps++;
            }

            Puts("Cleaned up " + removedTraps + " traps.");
            data.AutomatedTraps.Clear();
            data.Save();
        }

        /// <summary>
        /// Schedules the destruction of an automated trap and, optionally, replaces it with a new one.
        /// </summary>
        /// <param name="trap"> The AutomatedTrapData object containing information about the trap to be destroyed and replaced. </param>
        private void TryDestroyAndReplaceTrap(AutomatedTrapData trap)
        {
            // Schedule the trap for destruction after the specified time interval.
            timer.Once(config.AutomatedTrap.DestroyRevealedTrapAfterMinutes * 60, () =>
            {
                // Find the dummy stash associated with the trap and destroy it if found.
                StashContainer stash = FindEntityById(trap.DummyStash.Id) as StashContainer;
                stash?.Kill();

                // Find the dummy sleeping bag associated with the trap and destroy it if found.
                if (trap.DummySleepingBag != null)
                {
                    SleepingBag sleepingBag = FindEntityById(trap.DummySleepingBag.Id) as SleepingBag;
                    sleepingBag?.Kill();
                }

                // Remove the trap from the AutomatedTraps list.
                data.AutomatedTraps.Remove(trap.DummyStash.Id);

                // If specified in the config, spawn a new automated trap after the old one has been destroyed.
                if (config.AutomatedTrap.ReplaceRevealedTrap)
                    StartSpawnCoroutine();
            });
        }

        #endregion Traps Removal

        #region Traps Activation

        private void OnStashTriggered(StashContainer stash, BasePlayer player, bool stashWasDestroyed = false)
        {
            AutomatedTrapData trap = data.GetTrapData(stash.net.ID);
            if (trap != null)
            {
                if (!trap.DummyStash.Hidden)
                    return;

                data.UpdateTrapData(trap);
                TryDestroyAndReplaceTrap(trap);
            }

            else if (StashIsOwned(stash))
            {
                if (revealedOwnedStashes.Contains(stash.net.ID))
                    return;

                if (PlayerIsStashOwner(stash, player) || PlayerExistsInOwnerTeam(stash.OwnerID, player))
                    return;

                if (!stashWasDestroyed)
                    revealedOwnedStashes.Add(stash.net.ID);
            }

            lastRevealedStashPosition = stash.ServerPosition;
            data.CreateOrUpdatePlayerData(player);
            data.Save();

            if (config.Discord.PostIntoDiscord)
                SendDiscordReport(stash, player, stashWasDestroyed);

            if (data.GetPlayerRevealedTrapsCount(player) >= config.Moderation.RevealedTrapsTolerance)
                Ban(player);
        }

        private void HandleDestroyedStash(StashContainer stash)
        {
            // Find all building blocks within a certain radius of the stash position and add them to the list.
            List<BuildingBlock> nearbyBuildingBlocks = Pool.GetList<BuildingBlock>();
            Vis.Entities(stash.transform.position, 2.5f, nearbyBuildingBlocks, LayerMask.GetMask("Construction"), QueryTriggerInteraction.Ignore);

            // Skip early if no building blocks are found.
            if (!nearbyBuildingBlocks.Any())
                return;

            // Find the first building block whose owner can be found.
            BuildingBlock buildingBlock = nearbyBuildingBlocks.FirstOrDefault(b => FindPlayerById(b.OwnerID) != null);
            // Proceed if a building block with a known owner was found.
            if (buildingBlock != null)
            {
                BasePlayer buildingBlockOwner = FindPlayerById(buildingBlock.OwnerID);
                OnStashTriggered(stash, buildingBlockOwner, stashWasDestroyed: true);
            }

            // Free the memory used by the 'nearbyBuildingBlocks' list and release it back to the pool.
            Pool.FreeList(ref nearbyBuildingBlocks);
        }

        private void Ban(BasePlayer player)
        {
            if (Permission.Verify(player))
                return;

            timer.Once(config.Moderation.BanDelaySeconds, () =>
            {
                player.IPlayer.Ban(config.Moderation.BanReason);
                data.RemovePlayerData(player);
                data.Save();
            });
        }

        #region Stash Helper Functions

        private bool StashIsOwned(StashContainer stash)
        {
            return stash?.OwnerID != 0 ? true : false;
        }

        private bool PlayerIsStashOwner(StashContainer stash, BasePlayer player)
        {
            return stash?.OwnerID > 0 && player.userID == stash.OwnerID ? true : false;
        }

        private bool PlayerExistsInOwnerTeam(ulong stashOwnerId, BasePlayer player)
        {
            return player.Team != null && player.Team.members.Contains(stashOwnerId) ? true : false;
        }

        private BasePlayer FindPlayerById(ulong playerId)
        {
            return RelationshipManager.FindByID(playerId) ?? null;
        }

        private string GetGrid(Vector3 position)
        {
            return PhoneController.PositionToGridCoord(position);
        }

        #endregion Helper Functions

        #endregion

        #region Spawn Point Management

        /// <summary>
        /// Generates and manages spawn points for automated traps.
        /// </summary>
        public class SpawnPointManager
        {
            private HashSet<Tuple<Vector3, Quaternion>> availableSpawnPoints = new HashSet<Tuple<Vector3, Quaternion>>();

            /// <summary>
            /// Gets the count of available spawn points.
            /// </summary>
            public int AvailableSpawnPointsCount
            {
                get
                {
                    return availableSpawnPoints.Count;
                }
            }

            /// <summary>
            /// Generates random positions and creates spawn points for them.
            /// </summary>
            public IEnumerator GenerateSpawnPoints(int spawnPointsToGenerate)
            {
                // Determine the wait duration for the coroutine based on the current frame rate limit.
                WaitForSeconds waitDuration = ConVar.FPS.limit > 80 ? CoroutineEx.waitForSeconds(0.01f) : null;

                // Calculate the half size of the world.
                int halfWorldSize = ConVar.Server.worldsize / 2;
                // Keep track of the number of spawn points that were successfully generated.
                int successfullyGenerated = 0;
                // Keep track of the number of failed attempts to generate a spawn point.
                int failedAttempts = 0;

                // Attempt to find valid spawn points up to the specified number of times.
                for (int i = 0; i < config.SpawnPoint.MaximumAttemptsToFindSpawnPoints; i++)
                {
                    // Halt the generation of spawn points once the desired number is reached.
                    if (successfullyGenerated == spawnPointsToGenerate)
                    {
                        // Output the total number of generated spawn points to the console.
                        instance.Puts("Generated " + AvailableSpawnPointsCount + " spawn points.");
                        yield break;
                    }

                    // Generate a random position.
                    Vector3 randomPosition = Vector3.zero;
                    randomPosition.x = Random.Range(-halfWorldSize, halfWorldSize);
                    randomPosition.z = Random.Range(-halfWorldSize, halfWorldSize);
                    // Retrieve the height of the terrain at the given position.
                    randomPosition.y = TerrainMeta.HeightMap.GetHeight(randomPosition);

                    // Skip the position if it is not valid.
                    if (!PositionIsValid(randomPosition))
                    {
                        failedAttempts++;
                        continue;
                    }

                    // Create a spawn point for the position.
                    Tuple<Vector3, Quaternion> spawnPoint = FinalizeSpawnPoint(randomPosition);
                    availableSpawnPoints.Add(spawnPoint);

                    // Increment the number of successfully generated spawn points.
                    successfullyGenerated++;
                    // Wait for a set duration to prevent overloading the server with generating actions.
                    yield return waitDuration;
                }

                // Output the total number of generated spawn points to the console.
                instance.Puts("Generated " + AvailableSpawnPointsCount + " spawn points.");
                yield break;
            }

            /// <summary>
            /// Returns a random spawn point from the list of available spawn points.
            /// </summary>
            /// <returns> A tuple containing the position and rotation of the selected spawn point. </returns>
            public Tuple<Vector3, Quaternion> GetRandomSpawnPoint()
            {
                // Check if any spawn points are available and stop as soon as one is found.
                if (availableSpawnPoints.Any())
                {
                    // Select a random index from 0 to the number of available spawn points.
                    int randomSpawnPoint = Random.Range(0, AvailableSpawnPointsCount);
                    // Get the spawn point at the random index and remove it from the list to prevent it from being chosen again.
                    Tuple<Vector3, Quaternion> spawnPoint = availableSpawnPoints.ElementAt(randomSpawnPoint);
                    availableSpawnPoints.Remove(spawnPoint);

                    // Return the chosen spawn point.
                    return spawnPoint;
                }

                // If there are no spawn points available, return the default value of (0, 0, 0) for the position and the identity quaternion for the rotation.
                return Tuple.Create(Vector3.zero, Quaternion.identity);
            }

            /// <summary>
            /// Finds a child spawn point relative to the given spawn point.
            /// </summary>
            /// <param name="parentSpawnPoint"> The position of the parent spawn point. </param>
            /// <returns> A tuple containing the position and rotation of the child spawn point. </returns>
            public Tuple<Vector3, Quaternion> FindChildSpawnPoint(Vector3 parentPosition)
            {               
                // Generate a random point within a certain distance from the given spawn point.
                Vector2 randomPointInRange = ((Random.insideUnitCircle * 0.60f) + new Vector2(0.40f, 0.40f)) * config.SpawnPoint.PositionScanRadius;
                
                // Shift the random point to be relative to the parent spawn point, and adjust its height to match the terrain height at that spawn point.
                Vector3 childPosition = new Vector3(parentPosition.x + randomPointInRange.x, parentPosition.y, parentPosition.z + randomPointInRange.y);
                childPosition.y = TerrainMeta.HeightMap.GetHeight(childPosition);
                
                // Adjust the rotation.
                Tuple<Vector3, Quaternion> childSpawnPoint = FinalizeSpawnPoint(childPosition);
                return childSpawnPoint;
            }

            /// <summary>
            /// Clears the list of available spawn points.
            /// </summary>
            public void ClearAvailableSpawnPoints()
            {
                availableSpawnPoints.Clear();
            }

            /// <summary>
            /// Finalizes the position and rotation of a spawn point.
            /// </summary>
            /// <param name="position"> The position of the spawn point. </param>
            /// <returns> A tuple containing the final position and rotation of the spawn point. </returns>
            private Tuple<Vector3, Quaternion> FinalizeSpawnPoint(Vector3 position)
            {
                // Store the result of the linecast.
                RaycastHit hitInfo;
                // The start and end positions of the linecast.
                Vector3 linecast = new Vector3(0, 10f, 0);

                // Perform a linecast between the start and end positions.
                Physics.Linecast(position + linecast, position - linecast, out hitInfo, LayerMask.GetMask("Terrain"));

                // Calculate the rotation of the spawn point based on the linecast result.
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, hitInfo.normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                // Return the final position and rotation of the spawn point.
                return Tuple.Create(position, rotation);
            }

            /// <summary>
            /// Determines if a position is a valid spawn point.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is a valid spawn point, false otherwise. </returns>
            private bool PositionIsValid(Vector3 position)
            {
                if (PositionIsInWater(position) || !PositionIsOnTerrain(position))
                    return false;

                if (PositionIsInRestrictedBuildingZone(position) || PositionIsOnRoad(position))
                    return false;

                if (PositionIsOnCliff(position) || PositionIsOnRock(position) || PositionIsOnIce(position))
                    return false;

                if (PositionHasEntityNearby(position) || PositionHasPlayerInRange(position))
                    return false;

                return true;
            }

            /// <summary>
            /// Determines if a position is on terrain.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is on terrain, false otherwise. </returns>
            private bool PositionIsOnTerrain(Vector3 position)
            {
                // Check if a sphere at the position intersects with the Terrain layer.
                return Physics.CheckSphere(position, config.SpawnPoint.PositionScanRadius, LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore);
            }

            /// <summary>
            /// Determines if a position is in a restricted building zone.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is in a restricted building zone, false otherwise. </returns>
            private bool PositionIsInRestrictedBuildingZone(Vector3 position)
            {
                // Check if a sphere at the position intersects with the Prevent Building layer.
                return Physics.CheckSphere(position, config.SpawnPoint.PositionScanRadius, LayerMask.GetMask("Prevent Building"));
            }

            /// <summary>
            /// Determines if a position is on a road.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is on a road, false otherwise. </returns>
            private bool PositionIsOnRoad(Vector3 position)
            {
                // Get the terrain topology map.
                TerrainTopologyMap topology = TerrainMeta.TopologyMap;
                // Check if the position has road or roadside topology.
                if (topology.GetTopology(position, TerrainTopology.ROAD) || topology.GetTopology(position, TerrainTopology.ROADSIDE))
                    return true;

                return false;
            }

            /// <summary>
            /// Determines if a position is on a cliff.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is on a cliff, false otherwise. </returns>
            private bool PositionIsOnCliff(Vector3 position)
            {
                // Get the terrain topology map.
                TerrainTopologyMap topology = TerrainMeta.TopologyMap;
                // Check if the position has cliff or cliffside topology.
                if (topology.GetTopology(position, TerrainTopology.CLIFF) || topology.GetTopology(position, TerrainTopology.CLIFFSIDE))
                    return true;

                return false;
            }

            /// <summary>
            /// Determines if a position is in water.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is in water, false otherwise. </returns>
            private bool PositionIsInWater(Vector3 position)
            {
                // Check if the position is within the water level.
                return WaterLevel.Test(position);
            }

            /// <summary>
            /// Determines if a position is on an ice lake or sheet.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is on ice, false otherwise. </returns>
            private bool PositionIsOnIce(Vector3 position)
            {
                // Get a list of colliders in a sphere around the given position.
                List<Collider> colliders = Pool.GetList<Collider>();
                Vis.Colliders(position, config.SpawnPoint.PositionScanRadius, colliders, LayerMask.GetMask("World"), QueryTriggerInteraction.Ignore);
                
                // The result flag. Set to false by default.
                bool result = false;

                // Process each collider in the list one by one.
                if (colliders.Any())
                    foreach (Collider collider in colliders)
                    {
                        // Get the name of the collider.
                        string colliderName = collider.name.ToLower();
                        // Check if the collider is on an ice lake or ice sheet.
                        if (colliderName.Contains("ice_lake") || colliderName.Contains("ice_sheet"))
                        {
                            // Set the result flag to true if the collider is on an ice lake or ice sheet.
                            result = true;
                            break;
                        }
                    }

                // Free the memory used by the 'colliders' list and release it back to the pool.
                Pool.FreeList(ref colliders);
                return result;
            }

            /// <summary>
            /// Determines if the given position is on a rock formation.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is on rock formation, false otherwise. </returns>
            private bool PositionIsOnRock(Vector3 position)
            {
                // Get a list of colliders in a sphere around the given position.
                List<Collider> colliders = Pool.GetList<Collider>();
                Vis.Colliders(position, config.SpawnPoint.PositionScanRadius, colliders, LayerMask.GetMask("World"), QueryTriggerInteraction.Ignore);

                // The result flag. Set to false by default.
                bool result = false;

                // Process each collider in the list one by one.
                if (colliders.Any())
                    foreach (Collider collider in colliders)
                    {
                        // Get the name of the collider.
                        string colliderName = collider.name.ToLower();
                        // Check if the collider is on a rock or cliff-like formation.
                        if (colliderName.Contains("rock") || colliderName.Contains("cliff") || colliderName.Contains("formation"))
                        {
                            result = true;
                            break;
                        }
                    }

                // Free the memory used by the 'colliders' list and release it back to the pool.
                Pool.FreeList(ref colliders);
                return result;
            }

            /// <summary>
            /// Determines if there are any entities within the specified radius of the given position.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position has entities nearby, false otherwise. </returns>
            private bool PositionHasEntityNearby(Vector3 position)
            {
                // Get a list of entities within a given radius around the given position.
                List<BaseEntity> nearbyEntities = Pool.GetList<BaseEntity>();
                Vis.Entities(position, config.SpawnPoint.EntityDetectionRadius, nearbyEntities, LayerMask.GetMask("Construction", "Deployable", "Deployed"), QueryTriggerInteraction.Ignore);

                // Check if there are any entities in the list.
                bool result = nearbyEntities.Count > 0;
                Pool.FreeList(ref nearbyEntities);

                return result;
            }

            /// <summary>
            /// Determines if there are any players within a given radius around the given position.
            /// </summary>
            /// <param name="position"> The position to check for players around. </param>
            /// <returns>  True if there are players around the given position, false otherwise. </returns>
            private bool PositionHasPlayerInRange(Vector3 position)
            {
                // Get a list of players within a given radius around the given position.
                List<BasePlayer> nearbyPlayers = Pool.GetList<BasePlayer>();
                Vis.Entities(position, config.SpawnPoint.PlayerDetectionRadius, nearbyPlayers, LayerMask.GetMask("Player (Server)"), QueryTriggerInteraction.Ignore);

                // Result flag.
                bool result = false;

                // Go through each player in the list.
                if (nearbyPlayers.Any())
                    foreach (BasePlayer player in nearbyPlayers)
                    {
                        // Check if the player is not sleeping, is alive, and has a valid Steam id.
                        if (!player.IsSleeping() && player.IsAlive() && player.userID.IsSteamId())
                        {
                            result = true;
                            break;
                        }
                    }

                Pool.FreeList(ref nearbyPlayers);
                return result;
            }
        }

        #endregion Spawn Point Management

        #region Skin Management

        /// <summary>
        /// Provides utility methods for accessing and extracting the approved skins for a given item.
        /// </summary>
        public class SkinManager
        {
            // Stores the extracted skins of items, with the item's short name as the key and the skins as the value.
            private Dictionary<string, List<ulong>> extractedSkins = new Dictionary<string, List<ulong>>();

            /// <summary>
            /// Returns a list of approved skins for the specified item.
            /// </summary>
            /// <param name="itemDefinition"> The item to get the approved skins for. </param>
            /// <returns> The list of approved skins for the item. </returns>
            public List<ulong> GetSkinsForItem(ItemDefinition itemDefinition)
            {
                // Declare and initialize an empty list of skins.
                List<ulong> skins; // List<ulong> skins = new List<ulong>();
                // Get the item's short name.
                string itemShortName = itemDefinition.shortname;

                // If the extractedSkins dictionary doesn't contain the given item's short name as a key,
                // then extract the approved skins for the item and add them to the dictionary.
                if (!extractedSkins.TryGetValue(itemShortName, out skins))
                    skins = ExtractApprovedSkins(itemDefinition, skins);

                return skins;
            }

            /// <summary>
            /// Retrieves the workshop ids of approved skins for a given item.
            /// </summary>
            /// <param name="itemDefinition"> The item definition for which to extract approved skins. </param>
            /// <param name="skins"> An optional list of skins to append the extracted skins to. If not provided, a new list will be created and returned. </param>
            /// <returns> A list of workshop ids for the approved skins for the given item. </returns>
            private List<ulong> ExtractApprovedSkins(ItemDefinition itemDefinition, List<ulong> skins)
            {
                skins = Pool.GetList<ulong>(); 
                // Get the short name of the item.
                string itemShortName = itemDefinition.shortname;

                // Go through the list of approved skins.
                foreach (ApprovedSkinInfo skin in Approved.All.Values)
                {
                    // Skip skin if it is not for the current item.
                    if (skin.Skinnable.ItemName != itemShortName)
                        continue;

                    // Get the workshop id for the skin and add it to the list of skins.
                    ulong skinId = skin.WorkshopdId;
                    skins.Add(skinId);
                }

                // Save the list of skins for the current item.
                extractedSkins[itemShortName] = skins;
                return skins;
            }
        }

        #endregion Skin Management

        #region Discord Integration

        /// <summary>
        /// Formats the specified player's name and Steam profile link.
        /// </summary>
        /// <param name="player"> The player whose name and profile link should be formatted. </param>
        /// <returns> A string containing the formatted player name and profile link, or a default value if the player is invalid. </returns>
        private string FormatPlayerName(BasePlayer player)
        {
            if (!player.IsValid())
                return "Unknown Player";
            else
                return $"[{player.displayName}](https://steamcommunity.com/profiles/{player.userID})";
        }

        private int ParseColor(string hexadecimalColor)
        {
            // Try to convert the hexadecimal color value to an integer.
            int color;
            if (!int.TryParse(hexadecimalColor, NumberStyles.HexNumber, null, out color))
                // Set the default color value if the conversion fails.
                color = 16777215;

            return color;
        }

        private void SendDiscordReport(StashContainer stash, BasePlayer player, bool stashWasKilled)
        {
            Vector3 stashPosition = stash.ServerPosition;

            DiscordWebhook.Message message = new DiscordWebhook.Message
            {
                Content = StashIsOwned(stash) ? "A stash belonging to another player was revealed." : "An automated stash trap was found.",
            };

            DiscordWebhook.Embed embed = new DiscordWebhook.Embed
            {
                Color = ParseColor(config.Discord.EmbedColor),
                Footer = new DiscordWebhook.EmbedFooter
                {
                    Text = $"{covalence.Server.Name} | client.connect {covalence.Server.Address}:{covalence.Server.Port}",
                },
                Fields = new List<DiscordWebhook.EmbedField>
                {
                    new DiscordWebhook.EmbedField
                    {
                        Name = "Caught Player",
                        Value = $"{FormatPlayerName(player)}\n{player.UserIDString}",
                        Inline = false
                    },
                    new DiscordWebhook.EmbedField
                    {
                        Name = "Stash Found",
                        Value = StashIsOwned(stash) ? $"Player owned stash\nId: {stash.net.ID}" : $"Automated trap\nId: {stash.net.ID}",
                        Inline = false
                    },
                    new DiscordWebhook.EmbedField
                    {
                        Name = "Stash Position",
                        Value = $"Grid: {GetGrid(stashPosition)}\nCoordinates: {stashPosition}",
                        Inline = false
                    },
                    new DiscordWebhook.EmbedField
                    {
                        Name = "Violations",
                        Value = $"{data.GetPlayerRevealedTrapsCount(player)}",
                        Inline = false
                    },
                }
            };

            if (stashWasKilled)
            {
                DiscordWebhook.EmbedField stashWasKilledField = new DiscordWebhook.EmbedField
                {
                    Name = "Killed",
                    Value = "Stash was killed by placing a building block on top of it.",
                    Inline = false
                };
                embed.Fields.Insert(3, stashWasKilledField);
            }

            if (StashIsOwned(stash))
            {
                var stashOwner = FindPlayerById(stash.OwnerID);
                var stashOwnerName = stashOwner != null ? FormatPlayerName(stashOwner) : "Unknown";

                DiscordWebhook.EmbedField stashOwnerField = new DiscordWebhook.EmbedField
                {
                    Name = "Stash Owner",
                    Value = $"{stashOwnerName}\n{stash.OwnerID}",
                    Inline = false
                };
                embed.Fields.Insert(2, stashOwnerField);
            }

            message.AddEmbed(embed);

            // Puts($"Webhook URL: {config.Discord.WebhookUrl}");
            // Puts($"Message: {message.ToString()}");

            // Send a request to the Discord webhook url with the json-serialized message object.
            webrequest.Enqueue(config.Discord.WebhookUrl, message.ToString(), (headerCode, headerResult) =>
            {
                // Check the status code of the response
                if (headerCode >= 200 && headerCode <= 204)
                {
                    // Do something if the request was successful
                }
            }, this, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }

        private class DiscordWebhook
        {
            /// <summary>
            /// Represents a message that can be sent to a Discord channel.
            /// </summary>
            public class Message
            {
                /// <summary>
                /// The username of the Discord that will be displayed in the Discord channel.
                /// </summary>
                [JsonProperty("username")]
                public string Username { get; set; }

                /// <summary>
                /// The avatar url of the Discord that will be displayed in the Discord channel.
                /// </summary>
                [JsonProperty("icon_url")]
                public string IconUrl { get; set; }

                /// <summary>
                /// The content of the message that will be sent to the Discord channel.
                /// </summary>
                [JsonProperty("content")]
                public string Content { get; set; }

                /// <summary>
                /// The embedded content that will be displayed in the Discord channel.
                /// </summary>
                [JsonProperty("embeds")]
                public List<Embed> Embeds { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="Message"/> class with default property values.
                /// </summary>
                public Message()
                {
                    Content = string.Empty;
                    Username = string.Empty;
                    IconUrl = string.Empty;
                    Embeds = new List<Embed>();
                }

                /// <summary>
                /// Adds the specified embed object to this message object.
                /// </summary>
                /// <param name="embed"> The embed object to be added to this message object. </param>
                public void AddEmbed(Embed embed)
                {
                    Embeds.Add(embed);
                }

                /// <summary>
                /// Converts the Discord message into a json format.
                /// </summary>
                /// <returns> A json-serialized string representation of the message. </returns>
                public override string ToString()
                {
                    return JsonConvert.SerializeObject(this, new JsonSerializerSettings
                    {
                        DefaultValueHandling = DefaultValueHandling.Ignore
                    });
                }
            }

            /// <summary>
            /// Represents an embedded object that can be added to a Discord message.
            /// </summary>
            public class Embed
            {
                /// <summary>
                /// The title of the embedded content.
                /// </summary>
                [JsonProperty("title")]
                public string Title { get; set; }

                /// <summary>
                /// The description of the embedded content.
                /// </summary>
                [JsonProperty("description")]
                public string Description { get; set; }

                /// <summary>
                /// The url that will be linked to the title of the embedded content.
                /// </summary>
                [JsonProperty("url")]
                public string Url { get; set; }

                /// <summary>
                /// The color that will be used for the border of the embedded content.
                /// </summary>
                [JsonProperty("color")]
                public int Color { get; set; }

                /// <summary>
                /// The timestamp of when the embedded content was created.
                /// </summary>
                [JsonProperty("timestamp")]
                public string Timestamp { get; set; }

                /// <summary>
                /// The thumbnail image that will be displayed in the embedded content.
                /// </summary>
                [JsonProperty("thumbnail")]
                public EmbedThumbnail Thumbnail { get; set; }

                /// <summary>
                /// The author of the embedded content.
                /// </summary>
                [JsonProperty("author")]
                public EmbedAuthor Author { get; set; }

                /// <summary>
                /// The footer text and icon that will be displayed at the bottom of the embedded content.
                /// </summary>
                [JsonProperty("footer")]
                public EmbedFooter Footer { get; set; }

                /// <summary>
                /// The image that will be displayed in the embedded content.
                /// </summary>
                [JsonProperty("image")]
                public EmbedImage Image { get; set; }

                /// <summary>
                /// A list of fields that will be displayed in the embedded content.
                /// Each field consists of a title, value, and inline flag.
                /// </summary>
                [JsonProperty("fields")]
                public List<EmbedField> Fields { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="Embed"/> class with default property values.
                /// </summary>
                public Embed()
                {
                    // Set the default values for the properties.
                    Title = string.Empty;
                    Description = string.Empty;
                    Url = string.Empty;
                    Color = 0;
                    Timestamp = string.Empty;
                    Thumbnail = new EmbedThumbnail();
                    Author = new EmbedAuthor();
                    Footer = new EmbedFooter();
                    Image = new EmbedImage();
                    Fields = new List<EmbedField>();
                }

                /// <summary>
                /// Adds the specified field to the embedded content.
                /// </summary>
                /// <param name="field"> The field to be added. </param>
                public void AddField(EmbedField field)
                {
                    Fields.Add(field);
                }
            }

            /// <summary>
            /// Represents a field that can be added to a Discord embed.
            /// Each field consists of a title, value, and inline flag.
            /// </summary>
            public class EmbedField
            {
                /// <summary>
                /// The title of the field, which will be displayed above the value in the embedded content.
                /// </summary>
                [JsonProperty("name")]
                public string Name { get; set; }

                /// <summary>
                /// The value of the field, which will be displayed below the title in the embedded content.
                /// </summary>
                [JsonProperty("value")]
                public string Value { get; set; }

                /// <summary>
                /// A flag indicating whether the field should be displayed inline with other fields in the embedded content.
                /// If set to true, the field will be displayed inline. If set to false, the field will be displayed on a new line.
                /// </summary>
                [JsonProperty("inline")]
                public bool Inline { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="EmbedField"/> class with default property values.
                /// </summary>
                public EmbedField()
                {
                    // Set the default values for the properties.
                    Name = string.Empty;
                    Value = string.Empty;
                    Inline = false;
                }
            }

            /// <summary>
            /// Represents a thumbnail image in a Discord embed.
            /// </summary>
            public class EmbedThumbnail
            {
                /// <summary>
                /// The url of the thumbnail image.
                /// </summary>
                [JsonProperty("url")]
                public string AvatarUrl { get; set; }

                /// <summary>
                /// The width of the thumbnail image.
                /// </summary>
                [JsonProperty("width")]
                public int Width { get; set; }

                /// <summary>
                /// The height of the thumbnail image.
                /// </summary>
                [JsonProperty("height")]
                public int Height { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="EmbedThumbnail"/> class with default property values.
                /// </summary>
                public EmbedThumbnail()
                {
                    // Set the default values for the properties.
                    AvatarUrl = string.Empty;
                    Width = 0;
                    Height = 0;
                }
            }

            /// <summary>
            /// Represents an author in a Discord embed.
            /// </summary>
            public class EmbedAuthor
            {
                /// <summary>
                /// The name of the author.
                /// </summary>
                [JsonProperty("name")]
                public string Name { get; set; }

                /// <summary>
                /// The url of the author.
                /// </summary>
                [JsonProperty("url")]
                public string Url { get; set; }

                /// <summary>
                /// The url of the author's avatar.
                /// </summary>
                [JsonProperty("icon_url")]
                public string IconUrl { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="EmbedAuthor"/> class with default property values.
                /// </summary>
                public EmbedAuthor()
                {
                    // Set the default values for the properties.
                    Name = string.Empty;
                    Url = string.Empty;
                    IconUrl = string.Empty;
                }
            }

            /// <summary>
            /// Represents an image that can be added to a Discord embed.
            /// </summary>
            public class EmbedImage
            {
                // The url of the image that will be displayed in the embedded content.
                [JsonProperty("url")]
                public string AvatarUrl { get; set; }

                /// <summary>
                /// The width of the image.
                /// </summary>
                [JsonProperty("width")]
                public int Width { get; set; }

                /// <summary>
                /// The height of the image.
                /// </summary>
                [JsonProperty("height")]
                public int Height { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="EmbedImage"/> class with default values for its properties.
                /// </summary>
                public EmbedImage()
                {
                    // Set the default value for the url property.
                    AvatarUrl = string.Empty;
                    Width = 0;
                    Height = 0;
                }
            }

            /// <summary>
            /// Represents the footer text and icon that can be added to a Discord embed.
            /// </summary>
            public class EmbedFooter
            {
                /// <summary>
                /// The text that will be displayed at the bottom of the embedded content.
                /// </summary>
                [JsonProperty("text")]
                public string Text { get; set; }

                /// <summary>
                /// The url that will be linked to the footer text in the embedded content.
                /// </summary>
                [JsonProperty("icon_url")]
                public string IconUrl { get; set; }

                /// <summary>
                /// Initializes a new instance of the <see cref="EmbedFooter"/> class with default property values.
                /// </summary>
                public EmbedFooter()
                {
                    // Set the default values for the properties.
                    Text = string.Empty;
                    IconUrl = string.Empty;
                }
            }
        }

        #endregion Discord Integration

        #region Loot Editor

        /// <summary>
        /// Opens the loot editor for the given player and fills it with the current stash loot table.
        /// </summary>
        /// <param name="player"> The player to open the loot editor for. </param>
        private void OpenLootEditor(BasePlayer player)
        {
            // Verify the player is not already editing the stash loot table, and remove them if they are.
            StorageContainer storageContainer;
            if (activeLootEditors.TryGetValue(player, out storageContainer))
                RemoveLooter(player, storageContainer);

            // Create a new storage container for the player to use as a loot editor.
            storageContainer = CreateStorageEntity(storagePrefab);
            // Add the player mapped to the storage container to the 'activeLootEditors' dictionary.
            activeLootEditors.Add(player, storageContainer);

            // If the current loot table isn't empty, fill the storage container with its items.         
            if (config.StashLoot.LootTable != null)
                foreach (ItemInfo itemInfo in config.StashLoot.LootTable)
                {
                    // Create the item by its short name.
                    Item item = ItemManager.CreateByName(itemInfo.ShortName, itemInfo.MaximumSpawnAmount);
                    // Skip the item if it couldn't be created.
                    if (item == null)
                        continue;
                   
                    // Try to add the item to the storage container.
                    if (!item.MoveToContainer(storageContainer.inventory))
                        // Remove the item if it wasn't added successfully to avoid potential entities leak.
                        item.Remove();
                }

            // Finally, open the storage container's loot panel for the player after a short delay.
            timer.Once(1.0f, () =>
            {
                storageContainer.PlayerOpenLoot(player, doPositionChecks: false);
                Subscribe(nameof(OnPlayerLootEnd));
            });
        }

        /// <summary>
        /// Closes the loot editor for the given player and updates the stash loot table.
        /// </summary>
        /// <param name="inventory"> The inventory that the player is interacting with. </param>
        private void CloseLootEditor(PlayerLoot inventory)
        {
            // Obtain the player from the given inventory.
            BasePlayer player = inventory.GetComponent<BasePlayer>();

            // Try to obtain the storage container associated with the player.
            StorageContainer storageContainer;
            if (!activeLootEditors.TryGetValue(player, out storageContainer))
                return;

            // Verify the inventory source belongs to the storage container.
            if (inventory.entitySource == null || inventory.entitySource != storageContainer)
                return;

            // Update the stash loot table with the items in the storage container.
            UpdateStashLootTable(storageContainer);
            // Remove the player from the 'activeLootEditors' dictionary and destroy the storage container.
            RemoveLooter(player, storageContainer);
            Unsubscribe(nameof(OnPlayerLootEnd));
        }

        /// <summary>
        /// Updates the stash loot table based on the items in the given storage container.
        /// </summary>
        /// <param name="storageContainer"> The storage container containing the items to update the stash loot table with. </param>
        private void UpdateStashLootTable(StorageContainer storageContainer)
        {
            // Obtain a list of the items in the storage container.
            List<Item> containerItems = Pool.GetList<Item>();
            containerItems = storageContainer.inventory.itemList;

            // Initialize a list to store the updated stash loot table.
            List<ItemInfo> updatedLootTable = new List<ItemInfo>();
            for (int i = 0; i < containerItems.Count; i++)
            {
                // Get the current item that's being processed.
                Item item = containerItems[i];

                // Verify whether the item has already been added to the 'updatedLootTable' list.
                ItemInfo duplicateItem = updatedLootTable.FirstOrDefault(t => t.ShortName == item.info.shortname);
                // Skip the item if it has already been added.
                if (duplicateItem != null)
                    continue;
                // Otherwise, proceed to update the stash loot table with the item.
                else
                {
                    // Verify whether the item already exists in the current stash loot table.
                    ItemInfo existingItem = config.StashLoot.LootTable.FirstOrDefault(t => t.ShortName == item.info.shortname);
                    // If the item already exists, update its maximum and minimum spawn amounts.
                    if (existingItem != null)
                    {
                        existingItem.MinimumSpawnAmount = item.amount / 4;
                        existingItem.MaximumSpawnAmount = item.amount;
                    }

                    // Add the item to the 'updatedLootTable' list.
                    updatedLootTable.Add(new ItemInfo
                    {
                        ShortName = item.info.shortname,
                        MinimumSpawnAmount = item.amount < 4 ? 1 : item.amount / 4,
                        MaximumSpawnAmount = item.amount
                    });
                }
            }

            // Free the memory used by the 'containerItems' list and release it back to the pool.
            Pool.FreeList(ref containerItems);
            // Update the stash loot table with the new one.
            config.StashLoot.LootTable = new List<ItemInfo>(updatedLootTable);
            SaveConfig();
        }

        /// <summary>
        /// Revokes the given player's privilege as a loot editor and destroys the associated storage container.
        /// </summary>
        /// <param name="player"> The player to remove. </param>
        /// <param name="storageContainer"> The storage container belonging to the player. </param>
        private void RemoveLooter(BasePlayer player, StorageContainer storageContainer)
        {
            // Remove the player from the 'activeLootEditors' dictionary.
            activeLootEditors.Remove(player);

            // If the storage container exists, clear its inventory and destroy it.
            if (storageContainer != null)
            {
                storageContainer.inventory.Clear();
                storageContainer.Kill();
            }
        }

        /// <summary>
        /// Creates a storage entity from the specified prefab.
        /// </summary>
        /// <param name="prefabPath"> The path to the prefab to use for the storage entity. </param>
        /// <returns> The created storage entity, or null if the entity could not be created. </returns>
        private StorageContainer CreateStorageEntity(string prefabPath)
        {
            // Create the entity from the specified prefab.
            BaseEntity entity = GameManager.server.CreateEntity(prefabPath);
            // Don't proceed if the entity could not be created.
            if (entity == null)
                return null;

            // Convert the entity to a StorageContainer.
            StorageContainer storageContainer = entity as StorageContainer;
            if (storageContainer == null)
            {
                // Destroy the entity if it couldn't be converted.
                UnityEngine.Object.Destroy(entity);
                return null;
            }

            // Remove unnecessary components that would destroy the storage container when it's no longer
            // supported by the ground or when the ground beneath it disappears.
            UnityEngine.Object.DestroyImmediate(storageContainer.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(storageContainer.GetComponent<GroundWatch>());

            // Disable networking and saving.
            storageContainer.limitNetworking = true;
            storageContainer.EnableSaving(false);
            // Spawn the storage container.
            storageContainer.Spawn();

            return storageContainer;
        }

        #endregion Loot Editor

        #region Functions

        // Todo: Improve by adding a ddraw radius around the player
        private void DrawTraps(BasePlayer player, int drawDuration = 30)
        {
            if (!data.AutomatedTraps.Any())
                return;

            foreach (AutomatedTrapData trap in data.AutomatedTraps.Values)
            {
                player.SendConsoleCommand("ddraw.sphere", drawDuration, GetColor("#BDBDBD"), trap.DummyStash.Position, config.SpawnPoint.PositionScanRadius);

                player.SendConsoleCommand("ddraw.text", drawDuration, GetColor("#F2C94C"), trap.DummyStash.Position + new Vector3(0, 0.7f, 0), $"<size=30>{trap.DummyStash.Id}</size>");
                player.SendConsoleCommand("ddraw.sphere", drawDuration, GetColor("#BDBDBD"), trap.DummyStash.Position, 0.5f);

                if (trap.DummySleepingBag != null)
                {
                    player.SendConsoleCommand("ddraw.text", drawDuration, GetColor("#F2994A"), trap.DummySleepingBag.Position + new Vector3(0, 1.5f, 0), $"<size=30>{trap.DummySleepingBag.Id}</size>");
                    player.SendConsoleCommand("ddraw.sphere", drawDuration, GetColor("#BDBDBD"), trap.DummySleepingBag.Position, 1.3f);
                }
            }
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Searches the world for a BaseEntity object by its entity id.
        /// </summary>
        /// <param name="entityId"> The id of the entity to find. </param>
        /// <returns> The BaseEntity object with the specified id, or null if no such entity exists in the world or is valid. </returns>
        private BaseEntity FindEntityById(uint entityId)
        {
            BaseEntity entity = BaseNetworkable.serverEntities.Find(entityId) as BaseEntity;
            return !entity.IsValid() || entity.IsDestroyed ? null : entity;
        }

        /// <summary>
        /// Determines whether a chance with the given probability has succeeded.
        /// </summary>
        /// <param name="chance"> The probability of the chance. </param>
        /// <returns> True if the chance has succeeded, or false if it has failed. </returns>
        private bool ChanceSucceeded(int chance)
        {
            // Generate a random number between 0 and 100, and return true if the number is less than the given chance, or false otherwise.
            return Random.Range(0, 100) < chance ? true : false;
        }

        private Color GetColor(string hexColor)
        {
            if (string.IsNullOrEmpty(hexColor))
                hexColor = "#FFFFFFFF";

            string str = hexColor.Trim('#');

            if (str.Length == 3)
                str += str;

            if (str.Length == 6)
                str += "FF";

            if (str.Length != 8)
                str = "FFFFFFFF";

            byte r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
            byte a = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);

            return new Color32(r, g, b, a);
        }

        #endregion

        #region Permissions

        /// <summary>
        /// Contains utility methods for checking and registering plugin permissions.
        /// </summary>
        private static class Permission
        {
            // Permission required to use admin commands.
            public const string Admin = "automatedstashtraps.admin";

            /// <summary>
            /// Registers permissions used by the plugin.
            /// </summary>
            public static void Register()
            {
                instance.permission.RegisterPermission(Admin, instance);
            }

            /// <summary>
            /// Determines whether the given player has the specified permission.
            /// </summary>
            /// <param name="player"> The player to check. </param>
            /// <param name="permissionName"> The name of the permission to check. Defaults to the 'Admin' permission. </param>
            /// <returns> True if the player has the permission, false otherwise. </returns>
            public static bool Verify(BasePlayer player, string permissionName = Admin)
            {
                if (instance.permission.UserHasPermission(player.UserIDString, permissionName))
                    return true;

                // Lang: No permission
                return false;
            }
        }

        #endregion Permissions

        #region Commands

        private static class Command
        {
            public const string Give = "trap.give";
            public const string Loot = "trap.loot";
            public const string Draw = "trap.draw";
            public const string Report = "trap.report";
        }

        [ChatCommand(Command.Loot)]
        private void cmdLoot(BasePlayer player, string cmd, string[] args)
        {
            // Don't proceed if the player does not have permission to use the command.
            if (!Permission.Verify(player))
                return;

            OpenLootEditor(player);
        }

        [ConsoleCommand(Command.Give)]
        private void cmdGive(ConsoleSystem.Arg conArgs)
        {
            // Get the player who issued the command, and don't proceed if he is invalid.
            BasePlayer player = conArgs?.Player();
            if (!player.IsValid())
                return;

            // Don't proceed if the player does not have permission to use the command.
            if (!Permission.Verify(player))
                return;

            // Create the stash item with the specified amount.
            Item item = ItemManager.CreateByName("stash.small", 1);
            // Proceed if the item was created successfully.
            if (item != null)
            {
                // Add the item to the player's inventory, automatically determining the best container to put it in.
                // If there is no space in the inventory, the item will be dropped.
                player.GiveItem(item);
                manualTrapDeployers.Add(player);
            }
        }

        [ConsoleCommand(Command.Draw)]
        private void cmdDraw(ConsoleSystem.Arg conArgs)
        {
            // Get the player who issued the command, and don't proceed if he is invalid.
            BasePlayer player = conArgs?.Player();
            if (!player.IsValid())
                return;

            // Don't proceed if the player does not have permission to use the command.
            if (!Permission.Verify(player))
                return;

            DrawTraps(player);
        }

        [ConsoleCommand(Command.Report)]
        private void cmdReport(ConsoleSystem.Arg conArgs)
        {
            // Get the player who issued the command, and don't proceed if he is invalid.
            BasePlayer player = conArgs?.Player();
            if (!player.IsValid())
                return;

            // Don't proceed if the player does not have permission to use the command.
            if (!Permission.Verify(player))
                return;
        }

        #endregion Commands
    }
}