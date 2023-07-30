# Cheater hunting just got an upgrade! Are you ready?
Fed up with  ESP cheaters stealing your players' hard-earned loot? Say goodbye to that. AutomatedStashTrap keeps you one step ahead of them all the time. It's like a cheater magnet, catching cheaters left and right! So sit back, chill, and enjoy as they walk right into your traps.

![](https://i.imgur.com/ocUe4US.png)

-----

## Spawn points
When AutomatedStashTraps is loaded, it'll initiate a thorough map scan to pinpoint the ideal locations for trap deployment, ensuring a diverse distribution that keeps cheaters guessing.

The spawn point generator considers a wide range of terrain types, including cliffs, rocks, ice sheets, roads, and monuments, ensuring that traps are only spawned in believable locations where players can realistically build and deploy stashes, making it less likely for cheaters to suspect foul play.

-----

### Safe area radius
The safe area radius is a key element to consider. This radius is used as a guide to scan the surrounding area and evaluate various factors to determine the suitability of a location for trap spawning.

![](https://i.imgur.com/zD02NK9.png)

By adjusting this radius, you can fine-tune that traps are placed at a safe distance from player bases and structures. Furthermore, it prevents traps from spawning unexpectedly in front of players by detecting their presence in the area where the trap is to be spawned.

----

## Traps
### Stashes
The core component of each trap is the stash, which can also be accompanied by a dummy sleeping bag. These stashes will spawn pre-populated with loot to mimic player-placed stashes and are designed to remain active for extended periods, as they do not decay over time.

![](https://i.imgur.com/HNBu25P.png)

When a player exposes a stash, it's scheduled for destruction after a set amount of time. This prevents the trap from being immediately destroyed in front of the player's eyes. Once the stash is destroyed, a new trap is automatically spawned at a new location, ensuring that the maximum number of active traps is maintained at all times.

----

### Dummy sleeping bags
You can make your traps even more convincing with dummy sleeping bags. These sleeping bags will randomly spawn within the designated safe area radius of each trap.

![](https://i.imgur.com/gpsdt9g.png)

With dynamic skins and a pool of 5000 names to choose from, each sleeping bag will appear as if it were manually placed by a player, making each trap look more authentic.

![](https://i.imgur.com/1cA5el7.png)

-----

### Loot randomizer
Goodbye to the tedious task of configuring loot tables, hello to effortless loot setup. With AutomatedStashTraps, all you have to do is select the items you want to populate the stashes with and let the plugin do the rest.

![](https://i.imgur.com/VXLzBfp.png)

Whether you want the items to appear as blueprints, with skins, damaged or repaired, or even as broken items, the plugin automates the process with minimal configuration required.

----


## Automatic moderation
With customizable threshold levels, you can  enforce automatic bans on repeat offenders while still allowing for admin oversight and the ability to overturn decisions. This ensures accountability for cheaters, even when you're not able to monitor your server 24/7.

----


## Preventing exploits
Players have long used the tactic of building foundations on top of stashes to access their loot. Any such attempts are identified and flagged as violations, leaving no room for cheaters to slip through the cracks.

![](https://i.imgur.com/b0xXuqf.png)

-----

## One-click trap setup
The one-click trap setup allows for quick and easy deployment of traps in suspected cheater hotspots. With just one click, the plugin will automatically handle the setup process, including spawning the stash, populating it with items, and hiding it. This feature is handy for your moderators who may not have access to the F1 spawning menu.

-----

## Reports

### Modular Discord reports
Customize every aspect of your violation reports with the modular Discord reports feature, from the number of fields to the titles and their display. Whether you want to add multiple values together, remove fields or even create entirely empty reports, the choice is yours. The sky is the limit!

![](https://i.imgur.com/otQXPFc.png)

With 99% customization capability and built-in placeholders, you can tailor your reports to be truly unique and reflect your server's distinct identity.

### Placeholders

* $Player.Name
* $Player.Id
* $Player.Violations
* $Player.Team
* $Player.Connection.Time
* $Player.Address
* $Player.Combat.Id
* $Stash.Type
* $Stash.Id
* $Stash.Owner.Name
* $Stash.Owner.Id
* $Stash.Reveal.Method
* $Stash.Position.Coordinates
* $Stash.Position.Grid
* $Server.Name
* $Server.Address

-----

### Console reports
In addition to the detailed Discord reports, violations are also sent directly to the server console for a quick overview.

![](https://i.imgur.com/yhmueT2.png)

-----

### Reports Filter
The `StashReportFilter` option allows you to filter the type of stashes for which reports are sent. The possible values are:

* `0` - Sends reports only for automated traps.
* `1` - Sends reports only for player-owned stashes.
* `2` - Sends reports for both automated traps and player-owned stashes.

By default, the plugin sends reports for both automated traps and player-owned stashes. If you want to receive reports only for a specific type of stash, simply change the `StashReportFilter` value in the configuration file.

------

## Permissions
* `automatedstashtraps.admin` - Required for utilizing admin commands.
* `automatedstashtraps.ignore` - Players with this permission will not trigger violations upon opening stashes.


## Chat Commands
* `trap.loot` - Opens the loot editor, allowing you to customize the loot table of your automated stash traps.


## Console Commands
* `trap.give` - Quickly and easily deploys a stash trap.
* `trap.teleport` - Quickly jump to the location of the most recently revealed stash.
* `trap.draw [duration]` - Displays all automated traps with their corresponding IDs. Defaults to 30 seconds.

> The placeholder within `< >` indicates a required argument, while the placeholder within `[ ]` indicates an optional argument.

## Configuration
```json
{
  "Version": "1.5.0",
  "Spawn Point": {
    "Maximum Attempts To Find Spawn Points": 1500,
    "Safe Area Radius": 3.0,
    "Entity Detection Radius": 25.0,
    "Player Detection Radius": 25.0
  },
  "Automated Trap": {
    "Maximum Traps To Spawn": 50,
    "Destroy Revealed Trap After Minutes": 2,
    "Replace Revealed Trap": true,
    "Dummy Sleeping Bag": {
      "Spawn Along": true,
      "Spawn Proximity To Stash": 0.90,
      "Spawn Chance": 50,
      "Randomized Skin Chance": 40,
      "Randomized Nice Name Chance": 60
    }
  },
  "Violation": {
    "Reset On Wipe": true,
    "Can Teammate Ignore": false,
    "Can Clanmate Ignore": false
  },
  "Moderation": {
    "Automatic Ban": false,
    "Violations Tolerance": 3,
    "Ban Delay Seconds": 60,
    "Ban Reason": "Cheat Detected!"
  },
  "Notification": {
    "Prefix": "<color=#F2C94C>Automated Stash Trap</color>:",
    "Enable Console Report": true,
    "Stash Report Filter": 2
  },
  "Discord": {
    "Post Into Discord": false,
    "Webhook Url": "",
    "Report Interval": 60.0,
    "Message": "Cheater, cheater, pumpkin eater! Looks like someone's been caught breaking the rules!",
    "Embed Color": "#FFFFFF",
    "Embed Title": "A cheater has been spotted",
    "Embed Footer": "",
    "Embed Fields": [
      {
        "name": "Player Name",
        "value": "$Player.Name",
        "inline": true
      },
      {
        "name": "Id",
        "value": "$Player.Id",
        "inline": true
      },
      {
        "name": "Violations Count",
        "value": "$Player.Violations",
        "inline": true
      },
      {
        "name": "Revealed Stash Type",
        "value": "$Stash.Type",
        "inline": true
      },
      {
        "name": "Stash Id",
        "value": "$Stash.Id",
        "inline": true
      },
      {
        "name": "Grid",
        "value": "$Stash.Position.Grid",
        "inline": true
      },
      {
        "name": "Reveal Method",
        "value": "$Stash.Reveal.Method",
        "inline": false
      },
      {
        "name": "Stash Owner Name",
        "value": "$Stash.Owner.Name",
        "inline": true
      },
      {
        "name": "Stash Owner Id",
        "value": "$Stash.Owner.Id",
        "inline": true
      },
      {
        "name": "Team Info",
        "value": "$Player.Team",
        "inline": false
      },
      {
        "name": "Player Connection Time",
        "value": "$Player.Connection.Time",
        "inline": false
      },
      {
        "name": "Server",
        "value": "$Server.Name $Server.Address",
        "inline": false
      }
    ]
  },
  "Stash Loot": {
    "Minimum Loot Spawn Slots": 1,
    "Maximum Loot Spawn Slots": 6,
    "Spawn Chance As Blueprint": 10,
    "Spawn Chance With Skin": 50,
    "Spawn Chance As Damaged": 30,
    "Minimum Condition Loss": 5.0,
    "Maximum Condition Loss": 95.0,
    "Spawn Chance As Repaired": 15,
    "Spawn Chance As Broken": 5,
    "Loot Table": [
      {
        "Short Name": "scrap",
        "Minimum Spawn Amount": 25,
        "Maximum Spawn Amount": 125
      },
      {
        "Short Name": "metal.refined",
        "Minimum Spawn Amount": 15,
        "Maximum Spawn Amount": 40
      },
      {
        "Short Name": "cloth",
        "Minimum Spawn Amount": 60,
        "Maximum Spawn Amount": 200
      },
      {
        "Short Name": "cctv.camera",
        "Minimum Spawn Amount": 1,
        "Maximum Spawn Amount": 2
      },
      {
        "Short Name": "riflebody",
        "Minimum Spawn Amount": 1,
        "Maximum Spawn Amount": 3
      },
      {
        "Short Name": "techparts",
        "Minimum Spawn Amount": 1,
        "Maximum Spawn Amount": 6
      }
    ]
  }
}
```

----

## Localization
```json
{
  "Error.Permission": "You do not have the necessary permission to use this command.",
  "Trap.Reveal": "Hidden stash was found by <color=#F2C94C>{0}</color> at <color=#F2C94C>{1}</color>. Don't waste any time! Use <color=#F2994A>trap.teleport</color> to quickly jump to the site.",
  "Trap.Loot": "Stash loot table has been updated with a total of <color=#F2C94C>{0}</color> items.",
  "Trap.Draw": "Highlighting <color=#F2C94C>{0}</color> deployed traps on the map.",
  "Trap.Give": "You have received a stash trap. Simply place it on the ground to set it up.",
  "Trap.Setup": "Trap has been set up and filled with loot."
}
```

-----

## Uninstallation
Once the plugin has been unloaded, it will automatically remove all traps that were previously deployed, including any associated sleeping bags. This ensures that no stray traps or sleeping bags remain on the map after the plugin has been uninstalled.

---


## FAQ

### Why are no trap spawn points generated?
If, for any reason, the plugin fails to generate the desired number of spawn points or is unable to generate any spawn points at all, it may indicate that your map is either too small or too challenging. In such cases, you can either increase the maximum attempts to generate spawn points, increase the map size, or decrease the number of traps to be deployed.

-----


## For developers
### StashIsAutomatedTrap
```cs
bool StashIsAutomatedTrap(StashContainer stash)
```
Determines if a given stash is an automated trap created by this plugin. Returns true if the stash is an automated trap, false otherwise.

-----

## Keep the mod alive

Creating plugins is my passion, and I love nothing more than exploring new ideas and bringing them to the community. But it takes hours of work every day to maintain and improve these plugins that you have come to love and rely on.

With your support on [Patreon](https://www.patreon.com/VisEntities), you're giving me the freedom to devote more time and energy into what I love, which in turn allows me to continue providing new and exciting content to the community.

![](https://i.imgur.com/FfWSOqt.png)

A portion of the contributions will also be donated to the uMod team as a token of appreciation for their dedication to coding quality, inspirational ideas, and time spent for the community.

-------

## Credits
* Originally created and maintained ever since by **Dana**.
* Your work has been an inspiration, **WhiteThunder**. Thank you.