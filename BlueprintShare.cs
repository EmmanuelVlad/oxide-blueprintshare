using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Blueprint Share", "c_creep", "1.1.3")]
    [Description("Allows players to share researched blueprints with their friends, clan or team")]

    class BlueprintShare : CovalencePlugin
    {
        #region Fields

        [PluginReference] private Plugin Clans, ClansReborn, Friends;

		private List<string> playersDataList = new List<string>();

        private DynamicConfigFile playersData;

        private bool sharingEnabled = true, clansEnabled = true, friendsEnabled = true, teamsEnabled = true, enabledByDefault = true;

        #endregion

        #region Oxide Hooks

        private void Init()
        {
			permission.RegisterPermission("blueprintshare.toggle", this);

            LoadDefaultConfig();

            playersData = Interface.Oxide.DataFileSystem.GetFile("BlueprintShare");

            playersDataList = playersData.ReadObject<List<string>>();
        }

        private void OnPlayerInit(BasePlayer player)
        {
			sharingEnabled = playersDataList.Contains(player.UserIDString) ? !enabledByDefault : enabledByDefault;
        }

        private void OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player != null && action == "study" && (InClan(player.userID) || HasFriends(player.userID) || InTeam(player.userID)) && item.IsBlueprint() && sharingEnabled)
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
            List<BasePlayer> playersToShareWith = new List<BasePlayer>();
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

            foreach (BasePlayer sharePlayer in playersToShareWith)
            {
                if (sharePlayer == null || sharePlayer.blueprints.HasUnlocked(itemDefinition))
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

			return someoneLearnedTheBlueprint;
        }

        private ItemDefinition GetItemDefinition(string itemShortName)
        {
            if (string.IsNullOrEmpty(itemShortName)) return null;

            var itemDefinition = ItemManager.FindItemDefinition(itemShortName.ToLower());

            return itemDefinition;
        }

        #endregion

        #region Clan Methods

        private bool InClan(ulong playerUID)
        {
            if (ClansReborn == null && Clans == null) return false;

            var clanName = Clans?.Call<string>("GetClanOf", playerUID);

            return clanName != null;
        }

        private List<BasePlayer> GetClanMembers(ulong playerUID)
        {
            var membersList = new List<BasePlayer>();

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

                            var clanMember = RustCore.FindPlayerById(clanMemberUID);

                            membersList.Add(clanMember);
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

        private List<BasePlayer> GetFriends(ulong playerUID)
        {
            var friendsList = new List<BasePlayer>();

            var friends = Friends.Call<ulong[]>("GetFriends", playerUID);

            foreach (var friendUID in friends)
            {
                var friend = RustCore.FindPlayerById(friendUID);

                friendsList.Add(friend);
            }

            return friendsList;
        }

        #endregion

        #region Team Methods

        private bool InTeam(ulong playerUID)
        {
            var player = RustCore.FindPlayerById(playerUID);

            var playersCurrentTeam = RelationshipManager.Instance.FindTeam(player.currentTeam);

            return playersCurrentTeam != null;
        }

        private List<BasePlayer> GetTeamMembers(ulong playerUID)
        {
            var membersList = new List<BasePlayer>();

            var player = RustCore.FindPlayerById(playerUID);

            var playersCurrentTeam = RelationshipManager.Instance.FindTeam(player.currentTeam);

            var teamMembers = playersCurrentTeam.members;

            foreach (var teamMemberUID in teamMembers)
            {
                var teamMember = RustCore.FindPlayerById(teamMemberUID);

                membersList.Add(teamMember);
            }

            return membersList;
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
							player.Reply(GetLangValue("Prefix", playerUID) + GetLangValue((sharingEnabled) ? "ToggleOffMessage" : "ToggleOnMessage", playerUID));
							sharingEnabled = !sharingEnabled;

							if ((enabledByDefault && !sharingEnabled) ||
								(!enabledByDefault && sharingEnabled))
							{
								playersDataList.Add(playerUID);
							}
							else
							{
								playersDataList.Remove(playerUID);
							}

                            playersData.WriteObject(playersDataList);
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

        private bool SharingEnabled(string playerUID) => playersDataList.Contains(playerUID) ? !enabledByDefault : enabledByDefault;

        #endregion
    }
}