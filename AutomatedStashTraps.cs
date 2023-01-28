using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Libraries;
using Rust;
using Rust.Workshop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Automated Stash Traps", "Dana", "1.1.0")]
    [Description("Spawns fully automated stash traps across the map to catch ESP cheaters.")]
    public class AutomatedStashTraps : RustPlugin
    {
        #region Fields

        [PluginReference]
        private readonly Plugin Clans;

        private static AutomatedStashTraps instance;
        private static Configuration config;
        private static Data data;

        private SpawnPointManager spawnPointManager;
        private DiscordWebhook webhook;
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
        private const string BLUEPRINT_TEMPLATE = "blueprintbase";
        private const string STASH_PREFAB = "assets/prefabs/deployable/small stash/small_stash_deployed.prefab";
        private const string STORAGE_PREFAB = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
        private const string SLEEPING_BAG_PREFAB = "assets/prefabs/deployable/sleeping bag/sleepingbag_leather_deployed.prefab";

        // Last known position of a revealed stash.
        private Vector3 lastRevealedStashPosition;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty(PropertyName = "Version")]
            public string Version { get; set; }

            [JsonProperty(PropertyName = "Spawn Point")]
            public SpawnPointOptions SpawnPoint { get; set; }

            [JsonProperty(PropertyName = "Automated Trap")]
            public AutomatedTrapOptions AutomatedTrap { get; set; }

            [JsonProperty(PropertyName = "Violation")]
            public ViolationOptions Violation { get; set; }

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

            [JsonProperty(PropertyName = "Safe Area Radius")]
            public float SafeAreaRadius { get; set; }

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
        
        private class ViolationOptions
        {
            [JsonProperty(PropertyName = "Reset On Wipe")]
            public bool ResetOnWipe { get; set; }
            
            [JsonProperty(PropertyName = "Can Teammate Ignore")]
            public bool CanTeammateIgnore { get; set; }
            
            [JsonProperty(PropertyName = "Can Clanmate Ignore")]
            public bool CanClanmateIgnore { get; set; }
        }

        private class ModerationOptions
        {
            [JsonProperty(PropertyName = "Automatic Ban")]
            public bool AutomaticBan { get; set; }

            [JsonProperty(PropertyName = "Violations Tolerance")]
            public int ViolationsTolerance { get; set; }

            [JsonProperty(PropertyName = "Ban Delay Seconds")]
            public int BanDelaySeconds { get; set; }

            [JsonProperty(PropertyName = "Ban Reason")]
            public string BanReason { get; set; }
        }

        private class DiscordOptions
        {
            [JsonProperty(PropertyName = "Post Into Discord")]
            public bool Enable { get; set; }

            [JsonProperty(PropertyName = "Webhook Url")]
            public string WebhookUrl { get; set; }

            [JsonProperty(PropertyName = "Message")]
            public string Message { get; set; }

            [JsonProperty(PropertyName = "Embed Color")]
            public string EmbedColor { get; set; }

            [JsonProperty(PropertyName = "Embed Title")]
            public string EmbedTitle { get; set; }

            [JsonProperty(PropertyName = "Embed Footer")]
            public string EmbedFooter { get; set; }

            [JsonProperty(PropertyName = "Embed Fields")]
            public List<DiscordWebhook.EmbedField> EmbedFields { get; set; }

            [JsonIgnore]
            private int color;

            [JsonIgnore]
            private bool colorIsValidated;

            public int GetColor()
            {
                if (!colorIsValidated)
                {
                    if (!int.TryParse(EmbedColor.TrimStart('#'), NumberStyles.HexNumber, null, out color))
                        color = 16777215;

                    colorIsValidated = true;
                }
                return color;
            }
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
            public List<ItemInfo> LootTable { get; set; }
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

            // Inspired by WhiteThunder.
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
                Version = Version.ToString(),

                SpawnPoint = new SpawnPointOptions
                {
                    MaximumAttemptsToFindSpawnPoints = 1500,
                    SafeAreaRadius = 3f,
                    EntityDetectionRadius = 25f,
                    PlayerDetectionRadius = 25f
                },

                AutomatedTrap = new AutomatedTrapOptions
                {
                    MaximumTrapsToSpawn = 50,
                    DestroyRevealedTrapAfterMinutes = 2,
                    ReplaceRevealedTrap = true,
                    DummySleepingBag = new DummySleepingBagOptions
                    {
                        SpawnAlong = true,
                        SpawnChance = 50,
                        RandomizedSkinChance = 40,
                        RandomizedNiceNameChance = 60
                    }
                },

                Violation = new ViolationOptions
                {
                    ResetOnWipe = true,
                    CanTeammateIgnore = false,
                    CanClanmateIgnore = false
                },

                Moderation = new ModerationOptions
                {
                    AutomaticBan = false,
                    ViolationsTolerance = 3,
                    BanDelaySeconds = 60,
                    BanReason = "Cheat Detected!"
                },

                Discord = new DiscordOptions
                {
                    Enable = false,
                    WebhookUrl = string.Empty,
                    Message = "Cheater, cheater, pumpkin eater! Looks like someone's been caught breaking the rules!",
                    EmbedTitle = "A cheater has been spotted",
                    EmbedColor = "#FFFFFF",
                    EmbedFooter = string.Empty,
                    EmbedFields = new List<DiscordWebhook.EmbedField>()
                    {
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Player Name",
                            Value = "$Player.Name",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Id",
                            Value = "$Player.Id",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Violations Count",
                            Value = "$Player.Violations",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Revealed Stash Type",
                            Value = "$Stash.Type",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Stash Id",
                            Value = "$Stash.Id",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Grid",
                            Value = "$Stash.Position.Grid",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Reveal Method",
                            Value = "$Stash.Reveal.Method",
                            Inline = false
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Stash Owner Name",
                            Value = "$Stash.Owner.Name",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Stash Owner Id",
                            Value = "$Stash.Owner.Id",
                            Inline = true
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Team Info",
                            Value = "$Player.Team",
                            Inline = false
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Player Connection Time",
                            Value = "$Player.Connection.Time",
                            Inline = false
                        },
                        new DiscordWebhook.EmbedField
                        {
                            Name = "Server",
                            Value = "$Server.Name $Server.Address",
                            Inline = false
                        },
                    }
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

            if (string.Compare(config.Version, Version.ToString()) < 0)
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

            if (string.Compare(config.Version, "1.0.0") < 0)
                config = defaultConfig;

            if (string.Compare(config.Version, "1.1.0") < 0)
            {
                config.Violation = defaultConfig.Violation;
                config.Moderation = defaultConfig.Moderation;
            }

            PrintWarning("Configuration update complete! Updated from version " + config.Version + " to " + Version.ToString());
            config.Version = Version.ToString();
        }

        private void ValidateConfigValues()
        {
            PrintWarning("Validating configuration values...");

            if (config.AutomatedTrap.DestroyRevealedTrapAfterMinutes <= 0)
            {
                PrintError("Invalid trap removal time value. To avoid potential entity leaks, this value must be greater than 0. Default value of 5 will be applied.");
                config.AutomatedTrap.DestroyRevealedTrapAfterMinutes = 5;
            }

            if (config.Discord.Enable)
            {
                if (string.IsNullOrWhiteSpace(config.Discord.WebhookUrl) || !config.Discord.WebhookUrl.StartsWith("https://discord.com/api/webhooks/"))
                {
                    PrintError("Invalid webhook url provided. Please provide a valid webhook url to post into Discord.");
                    config.Discord.Enable = false;
                }

                if (string.IsNullOrWhiteSpace(config.Discord.EmbedColor) || !config.Discord.EmbedColor.StartsWith("#"))
                {
                    PrintError("Invalid color provided. The color must be a valid hex color code. Default color of #FFFFFF will be applied.");
                    config.Discord.EmbedColor = "#FFFFFF";
                }
            }

            if (config.StashLoot.MinimumLootSpawnSlots < 1)
            {
                PrintError("Invalid minimum loot spawn slots value. Default value of 1 will be applied.");
                config.StashLoot.MinimumLootSpawnSlots = 1;
            }

            if (config.StashLoot.MaximumLootSpawnSlots > 6)
            {
                PrintError("Invalid maximum loot spawn slots value. Default value of 6 will be applied.");
                config.StashLoot.MaximumLootSpawnSlots = 6;               
            }

            List<ItemInfo> invalidItems = config.StashLoot.LootTable.Where(item => item.GetItemDefinition() == null).ToList();
            foreach (ItemInfo invalidItem in invalidItems)
            {
                config.StashLoot.LootTable.Remove(invalidItem);
                PrintError("Invalid item '" + invalidItem.ShortName + "' removed from the loot table.");
            }

            foreach (ItemInfo item in config.StashLoot.LootTable)
            {
                if (item.MinimumSpawnAmount <= 0)
                {
                    PrintError("Invalid minimum spawn amount for item '" + item.ShortName + "'. Default value of 1 will be applied.");
                    item.MinimumSpawnAmount = 1;
                }

                if (item.MaximumSpawnAmount < item.MinimumSpawnAmount)
                {
                    PrintError("Invalid maximum spawn amount for item '" + item.ShortName + "'. Default value of " + item.MinimumSpawnAmount + " will be applied.");
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
            webhook = new DiscordWebhook();
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
        /// Hook: Called when a new save file is created.
        /// </summary>
        private void OnNewSave()
        {
            if (config.Violation.ResetOnWipe)
                Data.Clear();
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
            if (Permission.Verify(player, Permission.IGNORE))
                return;

            OnStashTriggered(stash, player, stashWasDestroyed: false);
        }

        /// <summary>
        /// Hook: Called when a player hides a stash.
        /// </summary>
        /// <param name="stash"> The stash that was hidden. </param>
        /// <param name="player"> The player who buried the stash. </param>
        private void OnStashHidden(StashContainer stash, BasePlayer player)
        {
            // To address a weird case scenario.
            if (PlayerIsStashOwner(stash, player))
                return;
            else if (!PlayerExistsInOwnerTeam(stash.OwnerID, player))
                revealedOwnedStashes.Add(stash.net.ID);
        }

        #endregion Oxide Hooks

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

        #region Trap Creation

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
            // If there are no traps to spawn, exit early.
            if (trapsToSpawn <= 0)
                yield break;

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
                StashContainer stash = CreateStashEntity(STASH_PREFAB, spawnPoint.Item1, spawnPoint.Item2);
                PopulateLoot(stash);

                // Initialize a sleeping bag entity, which may be spawned if the configuration allows it.
                SleepingBag sleepingBag = null;
                if (config.AutomatedTrap.DummySleepingBag.SpawnAlong && ChanceSucceeded(config.AutomatedTrap.DummySleepingBag.SpawnChance))
                {
                    // Find a nearby spawn point and create a sleeping bag at it.
                    Tuple<Vector3, Quaternion> nearbySpawnPoint = spawnPointManager.FindChildSpawnPoint(spawnPoint.Item1);
                    sleepingBag = CreateSleepingBagEntity(SLEEPING_BAG_PREFAB, nearbySpawnPoint.Item1, nearbySpawnPoint.Item2);
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
                    item = ItemManager.CreateByName(BLUEPRINT_TEMPLATE);
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

                // Remove the item if it wasn't added successfully to avoid any potential entity leak.
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

        #endregion Trap Creation

        #region Trap Removal

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

        #endregion Trap Removal

        #region Trap Trigger

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

                if (PlayerIsStashOwner(stash, player))
                    return;

                if (config.Violation.CanTeammateIgnore && PlayerExistsInOwnerTeam(stash.OwnerID, player))
                    return;

                if (config.Violation.CanClanmateIgnore && PlayerExistsInOwnerClan(stash.OwnerID, player))
                    return;

                if (!stashWasDestroyed)
                    revealedOwnedStashes.Add(stash.net.ID);
            }

            lastRevealedStashPosition = stash.ServerPosition;
            data.CreateOrUpdatePlayerData(player);
            data.Save();

            if (config.Moderation.AutomaticBan && data.GetPlayerRevealedTrapsCount(player) >= config.Moderation.ViolationsTolerance)
                BanPlayer(player);

            if (config.Discord.Enable)
                SendDiscordMessage(stash, player, stashWasDestroyed);
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
                if (Permission.Verify(buildingBlockOwner, Permission.IGNORE))
                    return;

                OnStashTriggered(stash, buildingBlockOwner, stashWasDestroyed: true);
            }

            // Free the memory used by the 'nearbyBuildingBlocks' list and release it back to the pool.
            Pool.FreeList(ref nearbyBuildingBlocks);
        }

        private void BanPlayer(BasePlayer player)
        {
            timer.Once(config.Moderation.BanDelaySeconds, () =>
            {
                player.IPlayer.Ban(config.Moderation.BanReason);
                data.RemovePlayerData(player);
                data.Save();
            });
        }

        #endregion Trap Trigger

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
                Vector2 randomPointInRange = ((Random.insideUnitCircle * 0.60f) + new Vector2(0.45f, 0.45f)) * config.SpawnPoint.SafeAreaRadius;
                
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
                return Physics.CheckSphere(position, config.SpawnPoint.SafeAreaRadius, LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore);
            }

            /// <summary>
            /// Determines if a position is in a restricted building zone.
            /// </summary>
            /// <param name="position"> The position to check. </param>
            /// <returns> True if the position is in a restricted building zone, false otherwise. </returns>
            private bool PositionIsInRestrictedBuildingZone(Vector3 position)
            {
                // Check if a sphere at the position intersects with the Prevent Building layer.
                return Physics.CheckSphere(position, config.SpawnPoint.SafeAreaRadius, LayerMask.GetMask("Prevent Building"));
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
                Vis.Colliders(position, config.SpawnPoint.SafeAreaRadius, colliders, LayerMask.GetMask("World"), QueryTriggerInteraction.Ignore);
                
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
                Vis.Colliders(position, config.SpawnPoint.SafeAreaRadius, colliders, LayerMask.GetMask("World"), QueryTriggerInteraction.Ignore);

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

            // Inspired by nivex.
            /// <summary>
            /// Returns a list of approved skins for the specified item.
            /// </summary>
            /// <param name="itemDefinition"> The item to get the approved skins for. </param>
            /// <returns> The list of approved skins for the item. </returns>
            public List<ulong> GetSkinsForItem(ItemDefinition itemDefinition)
            {
                List<ulong> skins;
                string itemShortName = itemDefinition.shortname;

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
                skins = new List<ulong>();
                string itemShortName = itemDefinition.shortname;

                foreach (ApprovedSkinInfo skin in Approved.All.Values)
                {
                    if (skin.Skinnable.ItemName != itemShortName)
                        continue;

                    ulong skinId = skin.WorkshopdId;
                    skins.Add(skinId);
                }

                extractedSkins[itemShortName] = skins;
                return skins;
            }
        }

        #endregion Skin Management

        #region Discord Integration

        private void SendDiscordMessage(StashContainer stash, BasePlayer player, bool stashWasKilled)
        {
            DiscordWebhook.Message message = new DiscordWebhook.Message
            {
                Content = config.Discord.Message,
            };

            DiscordWebhook.Embed embed = new DiscordWebhook.Embed
            {
                Color = config.Discord.GetColor(),
                Title = config.Discord.EmbedTitle,
                Footer = new DiscordWebhook.EmbedFooter
                {
                    Text = config.Discord.EmbedFooter,
                },

                EmbedFields = new List<DiscordWebhook.EmbedField>()
            };

            foreach (DiscordWebhook.EmbedField field in config.Discord.EmbedFields)
            {
                DiscordWebhook.EmbedField fieldToAdd = new DiscordWebhook.EmbedField()
                {
                    Name = field.Name,
                    Inline = field.Inline,
                    Value = Placeholder.ReplacePlaceholders(field.Value, player, stash, stashWasKilled)
                };
                embed.EmbedFields.Add(fieldToAdd);
            }

            message.Embeds.Add(embed);
            webhook.SendRequest(config.Discord.WebhookUrl, message);
        }

        private class DiscordWebhook
        {
            /// <summary>
            /// Sends a request to the Discord webhook url with the json-serialized message object.
            /// </summary>
            /// <param name="webhookUrl"> The url of the Discord webhook to send the message to. </param>
            /// <param name="message"> The message object to be serialized and sent as the body of the request. </param>
            public void SendRequest(string webhookUrl, Message message)
            {
                instance.webrequest.Enqueue(webhookUrl, message.ToString(), HandleRequestResponse, instance, RequestMethod.POST, new Dictionary<string, string> { { "Content-Type", "application/json" } });
            }

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
                    /*
                    Username = string.Empty;
                    IconUrl = string.Empty;
                    */
                    Content = string.Empty;
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
                        NullValueHandling = NullValueHandling.Ignore,
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
                public List<EmbedField> EmbedFields { get; set; }

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
                    EmbedFields = new List<EmbedField>();
                }

                /// <summary>
                /// Adds the specified field to the embedded content.
                /// </summary>
                /// <param name="field"> The field to be added. </param>
                public void AddField(EmbedField field)
                {
                    EmbedFields.Add(field);
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

            /// <summary>
            /// Handles the response received from the Discord webhook after sending a request.
            /// </summary>
            /// <param name="headerCode"> The http status code of the response. </param>
            /// <param name="headerResult"> The result message of the response. </param>
            private void HandleRequestResponse(int headerCode, string headerResult)
            {
                if (headerCode >= 200 && headerCode <= 204)
                    instance.Puts("Discord report sent successfully.");
                else
                {
                    switch (headerCode)
                    {
                        case 400:
                            instance.PrintError("Error: Bad request");
                            break;
                        case 401:
                            instance.PrintError("Error: Unauthorized");
                            break;
                        case 403:
                            instance.PrintError("Error: Forbidden");
                            break;
                        case 404:
                            instance.PrintError("Error: Not found");
                            break;
                        case 429:
                            instance.PrintError("Error: Rate limit reached");
                            break;
                        case 500:
                            instance.PrintError("Error: Internal server error");
                            break;
                        case 503:
                            instance.PrintError("Error: Service unavailable");
                            break;
                        default:
                            instance.PrintError("Error: " + headerResult);
                            break;
                    }
                }
            }
        }

        #endregion Discord Integration

        #region Placeholders

        public static class Placeholder
        {
            public const string PLAYER_NAME = "$Player.Name";
            public const string PLAYER_ID = "$Player.Id";
            public const string PLAYER_ADDRESS = "$Player.Address";
            public const string PLAYER_VIOLATIONS = "$Player.Violations";
            public const string PLAYER_TEAM = "$Player.Team";
            public const string PLAYER_CONNECTION_TIME = "$Player.Connection.Time";
            public const string PLAYER_COMBAT_ID = "$Player.Combat.Id";
            public const string STASH_TYPE = "$Stash.Type";
            public const string STASH_ID = "$Stash.Id";
            public const string STASH_OWNER_NAME = "$Stash.Owner.Name";
            public const string STASH_OWNER_ID = "$Stash.Owner.Id";
            public const string STASH_REVEAL_METHOD = "$Stash.Reveal.Method";
            public const string STASH_POSITION_COORDINATES = "$Stash.Position.Coordinates";
            public const string STASH_POSITION_GRID = "$Stash.Position.Grid";
            public const string SERVER_NAME = "$Server.Name";
            public const string SERVER_ADDRESS = "$Server.Address";

            /// <summary>
            /// Replaces the placeholders in the given text with their corresponding values.
            /// </summary>
            /// <param name="text"> The text containing placeholders to be replaced. </param>
            /// <returns></returns>
            public static string ReplacePlaceholders(string text, BasePlayer player, StashContainer stash, bool stashWasKilled)
            {
                text = text.Replace(PLAYER_NAME, instance.FormatPlayerName(player));
                text = text.Replace(PLAYER_ID, player.userID.ToString());
                text = text.Replace(PLAYER_ADDRESS, player.net.connection.ipaddress);
                text = text.Replace(PLAYER_VIOLATIONS, data.GetPlayerRevealedTrapsCount(player).ToString());
                text = text.Replace(STASH_OWNER_NAME, instance.StashIsOwned(stash) ? instance.FormatPlayerName(instance.FindPlayerById(stash.OwnerID)) : "Server");
                text = text.Replace(STASH_OWNER_ID, instance.StashIsOwned(stash) ? stash.OwnerID.ToString() : "0");
                text = text.Replace(STASH_TYPE, instance.StashIsOwned(stash) ? "Player owned stash" : "Automated trap");
                text = text.Replace(STASH_REVEAL_METHOD, stashWasKilled ? "Killed by placing a building block on top of it" : "Revealed normally");
                text = text.Replace(STASH_ID, stash.net.ID.ToString());
                text = text.Replace(STASH_POSITION_COORDINATES, stash.ServerPosition.ToString());
                text = text.Replace(STASH_POSITION_GRID, instance.GetGrid(stash.ServerPosition));
                text = text.Replace(PLAYER_TEAM, instance.FormatTeam(player));
                text = text.Replace(PLAYER_COMBAT_ID, player.net.ID.ToString());
                text = text.Replace(PLAYER_CONNECTION_TIME, instance.FormatConnectionTime(player));
                text = text.Replace(SERVER_NAME, instance.covalence.Server.Name);
                text = text.Replace(SERVER_ADDRESS, instance.covalence.Server.Address + ":" + instance.covalence.Server.Port);

                return text;
            }
        }

        #endregion Placeholders

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
            storageContainer = CreateStorageEntity(STORAGE_PREFAB);
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

            // Remove unnecessary components that would destroy the storage container when it's no longer supported by the ground.
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

        #region Draw Traps
      
        private void DrawTraps(BasePlayer player, int drawDuration)
        {
            drawDuration = drawDuration == 0 ? 30 : drawDuration;
            if (!data.AutomatedTraps.Any())
                return;

            foreach (AutomatedTrapData trap in data.AutomatedTraps.Values)
            {
                Vector3 stashPosition = trap.DummyStash.Position;

                Draw.Sphere(player, drawDuration, ParseColor("#BDBDBD", Color.white), stashPosition, config.SpawnPoint.SafeAreaRadius);
                Draw.Sphere(player, drawDuration, ParseColor("#BDBDBD", Color.white), stashPosition, 0.5f);
                Draw.Text(player, drawDuration, ParseColor("#F2C94C", Color.white), stashPosition + new Vector3(0, 0.7f, 0), $"<size=30>{trap.DummyStash.Id}</size>");

                if (trap.DummySleepingBag != null)
                {
                    Vector3 sleepingBagPosition = trap.DummySleepingBag.Position;

                    Draw.Sphere(player, drawDuration, ParseColor("#BDBDBD", Color.white), sleepingBagPosition, 1.3f);
                    Draw.Text(player, drawDuration, ParseColor("#F2994A", Color.white), sleepingBagPosition + new Vector3(0, 1.5f, 0), $"<size=30>{trap.DummySleepingBag.Id}</size>");

                    Draw.Arrow(player, drawDuration, ParseColor("#BDBDBD", Color.white), stashPosition, sleepingBagPosition, 0.50f);
                    Draw.Arrow(player, drawDuration, ParseColor("#BDBDBD", Color.white), sleepingBagPosition, stashPosition, 0.50f);
                }              
            }
        }

        // Inspired by WhiteThunder.
        private static class Draw
        {
            public static void Sphere(BasePlayer player, float duration, Color color, Vector3 originPosition, float radius)
            {
                player.SendConsoleCommand("ddraw.sphere", duration, color, originPosition, radius);
            }

            public static void Line(BasePlayer player, float duration, Color color, Vector3 originPosition, Vector3 targetPosition)
            {
                player.SendConsoleCommand("ddraw.line", duration, color, originPosition, targetPosition);
            }

            public static void Arrow(BasePlayer player, float duration, Color color, Vector3 originPosition, Vector3 targetPosition, float headSize)
            {
                player.SendConsoleCommand("ddraw.arrow", duration, color, originPosition, targetPosition, headSize);
            }

            public static void Text(BasePlayer player, float duration, Color color, Vector3 originPosition, string text)
            {
                player.SendConsoleCommand("ddraw.text", duration, color, originPosition, text);
            }
        }

        #endregion Draw Traps

        #region Helper Functions

        /// <summary>
        /// Checks if a plugin is present and loaded.
        /// </summary>
        /// <param name="plugin"> The plugin to check. </param>
        /// <returns> True if the plugin is loaded, false otherwise. </returns>
        private bool PluginIsLoaded(Plugin plugin)
        {
            return plugin != null && plugin.IsLoaded ? true : false;
        }

        /// <summary>
        /// Searches the map for an entity by its id.
        /// </summary>
        /// <param name="entityId"> The id of the entity to find. </param>
        /// <returns> The BaseEntity object with the specified id, or null if no such entity exists in the world or is valid. </returns>
        private BaseEntity FindEntityById(uint entityId)
        {
            BaseEntity entity = BaseNetworkable.serverEntities.Find(entityId) as BaseEntity;
            return !entity.IsValid() || entity.IsDestroyed ? null : entity;
        }

        /// <summary>
        /// Finds a player by their unique player id and returns the BasePlayer object.
        /// </summary>
        /// <param name="playerId"> The  id of the player to find. </param>
        /// <returns> The BasePlayer object of the player with the specified id, or null if not found. </returns>
        private BasePlayer FindPlayerById(ulong playerId)
        {
            return RelationshipManager.FindByID(playerId) ?? null;
        }

        /// <summary>
        /// Determines if the given stash container is owned by someone.
        /// </summary>
        /// <param name="stash"> The stash container to check ownership of. </param>
        /// <returns> True if the stash container is owned, false otherwise. </returns>
        private bool StashIsOwned(StashContainer stash)
        {
            return stash?.OwnerID != 0 ? true : false;
        }

        /// <summary>
        /// Determines if the given player is the owner of the given stash container.
        /// </summary>
        /// <param name="stash"> The stash container to check ownership of. </param>
        /// <param name="player"> The player to check if they are the owner. </param>
        /// <returns> True if the player is the owner of the stash container, false otherwise. </returns>
        private bool PlayerIsStashOwner(StashContainer stash, BasePlayer player)
        {
            return stash?.OwnerID > 0 && player.userID == stash.OwnerID ? true : false;
        }

        /// <summary>
        /// Determines if the given player is a member of the team that owns the given stash container.
        /// </summary>
        /// <param name="stashOwnerId"> The id of the owner of the stash container. </param>
        /// <param name="player"> The player to check if they are part of the owner's team. </param>
        /// <returns> True if the player is part of the owner's team, false otherwise. </returns>
        private bool PlayerExistsInOwnerTeam(ulong stashOwnerId, BasePlayer targetPlayer)
        {
            return targetPlayer.Team != null && targetPlayer.Team.members.Contains(stashOwnerId) ? true : false;
        }

        /// <summary>
        /// Check if the target player is a member of the clan the stash owner belongs to.
        /// </summary>
        /// <param name="stashOwnerId"> The user id of the stash owner. </param>
        /// <param name="targetPlayer"> The target player to check. </param>
        /// <returns> True if the target player is a member of the stash owner's clan, false otherwise. </returns>
        private bool PlayerExistsInOwnerClan(ulong stashOwnerId, BasePlayer targetPlayer)
        {
            if (PluginIsLoaded(Clans))
                return Clans.Call<bool>("IsClanMember", stashOwnerId.ToString(), targetPlayer.UserIDString);
            else
                PrintError("Clanmates are set to ignore violations, but the Clans plugin is not loaded. Load the plugin or update the config.");
            return false;
        }

        /// <summary>
        /// Retrieves the leader and teammates of the specified player.
        /// </summary>
        /// <param name="player"> The player to retrieve the team for. </param>
        /// <param name="teammates"> A list of teammates that will be populated with the player's teammates' ids. </param>
        /// <param name="leader">  A variable that will be set to the team leader's id. </param>
        private void GetTeam(BasePlayer player, List<ulong> teammates, out ulong leader)
        {
            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
            if (team == null)
            {
                leader = 0;
                return;
            }

            leader = team.teamLeader;
            teammates.AddRange(team.members);
        }

        /// <summary>
        /// Converts a Vector3 position to its corresponding grid coordinates.
        /// </summary>
        /// <param name="position"> The Vector3 position to convert to grid coordinates. </param>
        /// <returns> The grid coordinates of the specified position. </returns>
        private string GetGrid(Vector3 position)
        {
            return PhoneController.PositionToGridCoord(position);
        }

        /// <summary>
        /// Determines whether a chance with the given probability has succeeded.
        /// </summary>
        /// <param name="chance"> The probability of the chance. </param>
        /// <returns> True if the chance has succeeded, or false if it has failed. </returns>
        private bool ChanceSucceeded(int chance)
        {
            return Random.Range(0, 100) < chance ? true : false;
        }

        /// <summary>
        /// Attempts to parse a color from a given hexadecimal string and returns it. If parsing fails, returns the default color provided.
        /// </summary>
        /// <param name="hexadecimalColor"> The hexadecimal string representation of the color to parse. </param>
        /// <param name="defaultColor"> The default color to return in case of parsing failure. </param>
        /// <returns> The parsed color or the default color if parsing fails. </returns>
        private Color ParseColor(string hexadecimalColor, Color defaultColor)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(hexadecimalColor, out color) ? color : defaultColor;
        }

        /// <summary>
        /// Formats the specified player's name and Steam profile link.
        /// </summary>
        /// <param name="player"> The player whose name and profile link should be formatted. </param>
        /// <returns> A string containing the formatted player name and profile link, or a default value if the player is invalid. </returns>
        private string FormatPlayerName(BasePlayer player)
        {
            if (!player.IsValid())
                return "Unknown";
            else
                return $"[{player.displayName}](https://steamcommunity.com/profiles/{player.userID})";
        }

        /// <summary>
        /// Returns a string containing information about a player's team, including team leader and teammates' online status.
        /// </summary>
        /// <param name="player"> The player to retrieve team information for. </param>
        /// <returns> A string containing information about the player's team. </returns>
        private string FormatTeam(BasePlayer player)
        {
            ulong leader;
            List<ulong> teammates = Pool.GetList<ulong>();
            GetTeam(player, teammates, out leader);

            StringBuilder teamInfo = new StringBuilder();

            if (teammates.Count > 0)
            {
                foreach (ulong memberId in teammates)
                {
                    BasePlayer member = FindPlayerById(memberId);
                    if (member != null)
                    {
                        string onlineStatus = member.IsConnected ? "Online" : "Offline";
                        string isLeader = memberId == leader ? " Leader" : "";
                        teamInfo.AppendLine($"{FormatPlayerName(member)} {member.UserIDString} {onlineStatus} ({isLeader})");
                    }
                }
            }
            else
            {
                teamInfo.AppendLine("Player is not in a team.");
            }
            Pool.FreeList(ref teammates);
            return teamInfo.ToString();
        }

        /// <summary>
        /// Formats the connection time of the player in a human-readable format.
        /// </summary>
        /// <param name="player"> The player to format the connection time for. </param>
        /// <returns> The player's connection time in a human-readable format. </returns>
        private string FormatConnectionTime(BasePlayer player)
        {
            // Get the number of seconds that have passed since the player connected.
            float secondsConnected = player.Connection.GetSecondsConnected();
            // Check if the player has just connected.
            if (secondsConnected < 60)
                return "Just now";

            // Get the number of minutes that have passed.
            int minutesConnected = Mathf.FloorToInt(secondsConnected / 60);
            // Check if the player has been connected for less than an hour.
            if (minutesConnected < 60)
            {
                if (minutesConnected < 15)
                    return "Just now";
                else if (minutesConnected < 30)
                    return "About 15 min";
                else if (minutesConnected < 45)
                    return "About 30 min";
                else
                    return "About 45 min";
            }

            // Get the number of hours that have passed.
            int hoursConnected = Mathf.FloorToInt(minutesConnected / 60);
            // Check if the player has been connected for less than a day.
            if (hoursConnected < 24)
            {
                if (minutesConnected % 60 < 15)
                    return $"About {hoursConnected} hour{(hoursConnected > 1 ? "s" : "")}";
                else if (minutesConnected % 60 < 30)
                    return $"About {hoursConnected} hour{(hoursConnected > 1 ? "s" : "")} and 15 min";
                else if (minutesConnected % 60 < 45)
                    return $"About {hoursConnected} hour{(hoursConnected > 1 ? "s" : "")} and 30 min";
                else
                    return $"About {hoursConnected} hour{(hoursConnected > 1 ? "s" : "")} and 45 min";
            }

            // Get the number of days that have passed.
            int daysConnected = Mathf.FloorToInt(hoursConnected / 24);
            // Return the number of days and hours that have passed.
            return $"{daysConnected} day{(daysConnected > 1 ? "s" : "")} and {hoursConnected % 24} hour{((hoursConnected % 24) > 1 ? "s" : "")}";
        }

        #endregion

        #region Permissions

        /// <summary>
        /// Contains utility methods for checking and registering plugin permissions.
        /// </summary>
        private static class Permission
        {
            // Permission required to use admin commands.
            public const string ADMIN = "automatedstashtraps.admin";
            public const string IGNORE = "automatedstashtraps.ignore";
            
            /// <summary>
            /// Registers permissions used by the plugin.
            /// </summary>
            public static void Register()
            {
                instance.permission.RegisterPermission(ADMIN, instance);
                instance.permission.RegisterPermission(IGNORE, instance);
            }

            /// <summary>
            /// Determines whether the given player has the specified permission.
            /// </summary>
            /// <param name="player"> The player to check. </param>
            /// <param name="permissionName"> The name of the permission to check. Defaults to the 'Admin' permission. </param>
            /// <returns> True if the player has the permission, false otherwise. </returns>
            public static bool Verify(BasePlayer player, string permissionName = ADMIN)
            {
                if (instance.permission.UserHasPermission(player.UserIDString, permissionName))
                    return true;
                
                // Lang: no permission
                return false;
            }
        }

        #endregion Permissions

        #region Commands

        private static class Command
        {
            public const string GIVE = "trap.give";
            public const string LOOT = "trap.loot";
            public const string DRAW = "trap.draw";
        }

        [ConsoleCommand(Command.GIVE)]
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
                player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                manualTrapDeployers.Add(player);
            }
        }

        [ChatCommand(Command.LOOT)]
        private void cmdLoot(BasePlayer player, string cmd, string[] args)
        {
            if (!Permission.Verify(player))
                return;

            OpenLootEditor(player);
        }

        [ConsoleCommand(Command.DRAW)]
        private void cmdDraw(ConsoleSystem.Arg conArgs)
        {
            BasePlayer player = conArgs?.Player();
            if (!player.IsValid())
                return;

            if (!Permission.Verify(player))
                return;

            int drawDuration = 0;
            if (conArgs.HasArgs())
                drawDuration = conArgs.GetInt(0);

            DrawTraps(player, drawDuration);
        }

        #endregion Commands
    }
}
