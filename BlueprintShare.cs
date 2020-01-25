using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Blueprint Share", "c_creep & evlad", "1.2.0")]
    [Description("Allows players to share researched blueprints with their friends, clan or team")]

    class BlueprintShare : CovalencePlugin
    {
        #region Fields

        [PluginReference] private Plugin Clans, ClansReborn, Friends;

        private bool clansEnabled = true, friendsEnabled = true, teamsEnabled = true, enabledByDefault = true;

		private class StoredData
		{
			private string FileName = "BlueprintShare";
			public Dictionary<string, List<string>> Offline = new Dictionary<string, List<string>>();
			public List<string> Toggle = new List<string>();

			public StoredData()
			{
			}

			public StoredData Read()
			{
				return Interface.Oxide.DataFileSystem.ReadObject<StoredData>(FileName);
			}

			public void Write()
			{
				Interface.Oxide.DataFileSystem.WriteObject(FileName, this);
			}
		}
		private StoredData storedData;

        #endregion

        #region Oxide Hooks

        private void Init()
        {
			permission.RegisterPermission("blueprintshare.toggle", this);

            LoadDefaultConfig();

			storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("BlueprintShare");
        }
		

        private void OnPlayerInit(BasePlayer player)
        {
			if (storedData.Offline.ContainsKey(player.UserIDString))
			{
				if (SharingEnabled(player.UserIDString))
				{
					bool hasUnlockedAtLeastOne = false;
					foreach (var itemShortName in storedData.Offline[player.UserIDString])
					{
						ItemDefinition blueprint = GetItemDefinition(itemShortName);
						if (player.blueprints.HasUnlocked(blueprint))
							continue;
						hasUnlockedAtLeastOne = true;
						player.blueprints.Unlock(blueprint);
						player.Command("chat.add", 0, 0, GetLangValue("NewBlueprintLearned", player.UserIDString, player.displayName, blueprint.displayName.translated));
					}
					if (hasUnlockedAtLeastOne)
					{
						EffectNetwork.Send(
							new Effect("assets/prefabs/deployable/research table/effects/research-success.prefab", player.transform.position, Vector3.zero),
							player.net.connection
						);
					}
				}
				storedData.Offline.Remove(player.UserIDString);
				storedData.Write();
			}
        }

        private void OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player != null && action == "study" && (InClan(player.userID) || HasFriends(player.userID) || InTeam(player.userID)) && item.IsBlueprint() && SharingEnabled(player.UserIDString))
            {
                var itemShortName = item.blueprintTargetDef.shortname;

                if (string.IsNullOrEmpty(itemShortName)) return;

                if (UnlockBlueprint(player, itemShortName))
				{
                	item.Remove();
				}
            }
        }

        #endregion

        #region Config

        protected override void LoadDefaultConfig()
        {
            Config["ClansEnabled"] = clansEnabled = GetConfigValue("ClansEnabled", true);
            Config["FriendsEnabled"] = friendsEnabled = GetConfigValue("FriendsEnabled", true);
            Config["TeamsEnabled"] = teamsEnabled = GetConfigValue("TeamsEnabled", true);
            Config["EnableByDefault"] = enabledByDefault = GetConfigValue("EnableByDefault", true);

            SaveConfig();
        }

        private T GetConfigValue<T>(string name, T defaultValue)
        {
            return Config[name] == null ? defaultValue : (T)Convert.ChangeType(Config[name], typeof(T));
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "[#D85540][Blueprint Share] [/#]",
                ["ArgumentsErrorMessage"] = "Error, incorrect arguments. Try /bs help.",
                ["HelpMessage"] = "[#D85540]Blueprint Share Help:[/#]\n\n[#D85540]/bs toggle[/#] - Toggles the sharing of blueprints.",
                ["ToggleOnMessage"] = "You have enabled sharing blueprints.",
                ["ToggleOffMessage"] = "You have disabled sharing blueprints.",
                ["NoPermissionMessage"] = "You don't have permission to use this command!",
				["NewBlueprintLearned"] = "[#55aaff]{0} learned a new blueprint: <size=18>{1}</size>[/#]"
            }, this);
        }

        private string GetLangValue(string key, string id = null, params object[] args) => covalence.FormatText(string.Format(lang.GetMessage(key, this, id), args));

        #endregion

        #region General Methods

        private bool UnlockBlueprint(BasePlayer player, string itemShortName)
        {
			bool someoneLearnedTheBlueprint = false;
            ulong playerUID = player.userID;
            List<ulong> playersToShareWith = new List<ulong>();
			List<ulong> offlinePlayers = new List<ulong>();
			ItemDefinition itemDefinition = GetItemDefinition(itemShortName);

			if (itemDefinition == null) return false;

            if (clansEnabled && (Clans != null || ClansReborn != null) && InClan(playerUID))
            {
                playersToShareWith.AddRange(GetClanMembers(playerUID));
            }

            if (friendsEnabled && Friends != null && HasFriends(playerUID))
            {
                playersToShareWith.AddRange(GetFriends(playerUID));
            }
            
            if (teamsEnabled && InTeam(playerUID))
            {
                playersToShareWith.AddRange(GetTeamMembers(playerUID));
            }

            foreach (ulong sharePlayerID in playersToShareWith)
            {
				BasePlayer sharePlayer = RustCore.FindPlayerById(sharePlayerID);
				if (sharePlayer == null || !sharePlayer.IsConnected)
					offlinePlayers.Add(sharePlayerID);
                if (sharePlayer == null || !sharePlayer.IsConnected || sharePlayer.blueprints.HasUnlocked(itemDefinition) || !SharingEnabled(sharePlayer.UserIDString))
					continue;
				someoneLearnedTheBlueprint = true;
				sharePlayer.blueprints.Unlock(itemDefinition);
				if (player.userID != sharePlayer.userID) {
					sharePlayer.Command("chat.add", 0, 0, GetLangValue("NewBlueprintLearned", sharePlayer.UserIDString, player.displayName, itemDefinition.displayName.translated));
				}
				EffectNetwork.Send(
					new Effect("assets/prefabs/deployable/research table/effects/research-success.prefab", sharePlayer.transform.position, Vector3.zero),
					sharePlayer.net.connection
				);
            }
			if (someoneLearnedTheBlueprint)
			{
				foreach (ulong sharePlayerID in offlinePlayers)
				{
					AddBlueprintToOfflinePlayer(sharePlayerID.ToString(), itemShortName);
				}
				storedData.Write();
			}

			return someoneLearnedTheBlueprint;
        }

        private ItemDefinition GetItemDefinition(string itemShortName)
        {
            if (string.IsNullOrEmpty(itemShortName)) return null;

            var itemDefinition = ItemManager.FindItemDefinition(itemShortName.ToLower());

            return itemDefinition;
        }

		private bool AddBlueprintToOfflinePlayer(string playerID, string itemShortName)
		{
			if (!storedData.Offline.ContainsKey(playerID)) {
				storedData.Offline.Add(playerID, new List<string>(){itemShortName});
				return true;
			}
			if (!storedData.Offline[playerID].Contains(itemShortName)) {
				storedData.Offline[playerID].Add(itemShortName);
				return true;
			}
			return false;
		}

        #endregion

        #region Clan Methods

        private bool InClan(ulong playerUID)
        {
            if (ClansReborn == null && Clans == null) return false;

            var clanName = Clans?.Call<string>("GetClanOf", playerUID);

            return clanName != null;
        }

        private List<ulong> GetClanMembers(ulong playerUID)
        {
            var membersList = new List<ulong>();

            var clanName = Clans?.Call<string>("GetClanOf", playerUID);

            if (!string.IsNullOrEmpty(clanName))
            {
                var clan = Clans?.Call<JObject>("GetClan", clanName);

                if (clan != null && clan is JObject)
                {
                    var members = (clan as JObject).GetValue("members");

                    if (members != null && members is JArray)
                    {
                        foreach (var member in (JArray)members)
                        {
                            ulong clanMemberUID;

                            if (!ulong.TryParse(member.ToString(), out clanMemberUID)) continue;

                            membersList.Add(clanMemberUID);
                        }
                    }
                }
            }
            return membersList;
        }

        #endregion

        #region Friends Methods

        private bool HasFriends(ulong playerUID)
        {
            if (Friends == null) return false;

            var friendsList = Friends.Call<ulong[]>("GetFriends", playerUID);

            return friendsList != null && friendsList.Length != 0;
        }

        private List<ulong> GetFriends(ulong playerUID)
        {
			return new List<ulong>(Friends.Call<ulong[]>("GetFriends", playerUID));
        }

        #endregion

        #region Team Methods

        private bool InTeam(ulong playerUID)
        {
            var player = RustCore.FindPlayerById(playerUID);

            var playersCurrentTeam = RelationshipManager.Instance.FindTeam(player.currentTeam);

            return playersCurrentTeam != null;
        }

        private List<ulong> GetTeamMembers(ulong playerUID)
        {
            var membersList = new List<BasePlayer>();

            var player = RustCore.FindPlayerById(playerUID);

            var playersCurrentTeam = RelationshipManager.Instance.FindTeam(player.currentTeam);

			return playersCurrentTeam.members;
        }

        #endregion

        #region Chat Commands

        [Command("blueprintshare", "bs")]
        private void ToggleCommand(IPlayer player, string command, string[] args)
        {
            var playerUID = player.Id;

            if (args.Length < 1)
            {
                player.Reply(GetLangValue("Prefix", playerUID) + GetLangValue("ArgumentsErrorMessage", playerUID));

                return;
            }

            switch (args[0].ToLower())
            {
                case "help":
                    {
                        player.Reply(GetLangValue("HelpMessage", playerUID));

                        break;
                    }
                case "toggle":
                    {
                        if (permission.UserHasPermission(playerUID, "blueprintshare.toggle"))
                        {
							var hasSharingEnabled = SharingEnabled(playerUID);
							player.Reply(GetLangValue("Prefix", playerUID) + GetLangValue((hasSharingEnabled) ? "ToggleOffMessage" : "ToggleOnMessage", playerUID));

							if ((enabledByDefault && hasSharingEnabled) ||
								(!enabledByDefault && !hasSharingEnabled))
							{
								storedData.Toggle.Add(playerUID);
							}
							else
							{
								storedData.Toggle.Remove(playerUID);
							}
							storedData.Write();
                        }
                        else
                        {
                            player.Reply(GetLangValue("Prefix", playerUID) + GetLangValue("NoPermissionMessage", playerUID));
                        }
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }

        #endregion

        #region API

        private bool SharingEnabled(string playerUID) => storedData.Toggle.Contains(playerUID) ? !enabledByDefault : enabledByDefault;

        #endregion
    }
}