using Steamworks.Data;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Steamworks;
using MenuLib;
using MenuLib.MonoBehaviors;
using System.Reflection;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.UI;
using static MenuManager;
using UnityEngine.Events;
using System.Numerics;
using TMPro;

namespace RepoCustomPhotonServer;

[BepInPlugin("RepoCustomPhotonServer", "Custom Photon Server", "1.0.2")]
internal sealed class RepoCustomPhotonServer : BaseUnityPlugin
{
    internal static readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("Photon fixer");

    public ConfigEntry<bool> isActive;
    public ConfigEntry<bool> isChangeSteamAppId;
    public ConfigEntry<string> AppIdRealtime;
    public ConfigEntry<string> AppIdVoice;
    public static RepoCustomPhotonServer Instance { get; private set; }

    internal static AuthTicket steamAuthTicket;

    private void Awake()
    {
        Instance = this;
        logger.LogInfo("RepoCustomPhotonServer Loaded!");

        isActive = Config.Bind("General", "Enable", false);
        isChangeSteamAppId = Config.Bind("General", "Change SteamAppId", false);
        AppIdRealtime = Config.Bind("Photon AppIDs", "AppIdRealtime", "", new ConfigDescription("Photon Realtime App ID", null));
        AppIdVoice = Config.Bind("Photon AppIDs", "AppIdVoice", "", new ConfigDescription("Photon Voice App ID", null));

        var harmony = new Harmony("RepoCustomPhotonServer");
        harmony.PatchAll();
    }

    private static bool checkAppID(string input , bool appID)
    {
        if (!Guid.TryParseExact(input ?? "", "D", out _))
        {
            if (appID)
            {
                MenuManager.instance.PagePopUp("Invalid AppID", UnityEngine.Color.red,
                "Invalid AppIdVoice format. It must be in the format of a Guid (e.g., h26742gw-96e2-4c18-93c4-978176705b0c).","OK.", false);
            }
            else
            {
                MenuManager.instance.PagePopUp("Invalid AppID", UnityEngine.Color.red,
                    "Invalid AppIdRealtime format. It must be in the format of a Guid (e.g., h26742gw-96e2-4c18-93c4-978176705b0c).","OK.", false);
            }
            return false;
        }
        return true;
    }

    [HarmonyPatch]
    static class FixRepoConfig
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("REPOConfig.ConfigMenu"),
                "CreateModEntries",
                new Type[] { typeof(REPOPopupPage), typeof(ConfigEntryBase[]) }
            );

        [HarmonyPrefix]
        static bool Prefix(REPOPopupPage modPage, ConfigEntryBase[] configEntryBases)
        {
            var sectionGroups = configEntryBases.GroupBy(entry => entry.Definition.Section);
            const string targetSection = "Photon AppIDs";

            if (!configEntryBases.Any(entry => entry.Definition.Section == targetSection))
            {
                return true;
            }


            ConfigEntryBase[] filteredConfigEntryBases = configEntryBases
                .Where(entry => entry.Definition.Section != targetSection)
                .ToArray();

            var configMenuType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "REPOConfig.ConfigMenu");
            if (configMenuType != null)
            {
                var createModEntriesMethod = configMenuType.GetMethod("CreateModEntries", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (createModEntriesMethod != null)
                {
                    createModEntriesMethod.Invoke(null, new object[] { modPage, filteredConfigEntryBases });
                }
            }


            modPage.AddElementToScrollView(scrollView =>
            {
                var repoLabel = MenuAPI.CreateREPOLabel(targetSection, scrollView);
                repoLabel.labelTMP.fontStyle = FontStyles.Bold;
                return repoLabel.rectTransform;
            });

            string AppIdRealtime = Instance.AppIdRealtime.Value;
            string AppIdVoice = Instance.AppIdVoice.Value;
            REPOInputField appIdRealtimeField = null;
            REPOInputField appIdVoiceField = null;

            modPage.AddElementToScrollView(scrollView =>
            {
                appIdRealtimeField = MenuAPI.CreateREPOInputField("AppId Realtime", s =>
                {
                    var inputValue = string.IsNullOrEmpty(s) ? null : s.ToLower().Trim();
                    AppIdRealtime = inputValue;
                }, scrollView, defaultValue: Instance.AppIdRealtime.Value);

                return appIdRealtimeField.rectTransform;
            });

            modPage.AddElementToScrollView(scrollView =>
            {
                appIdVoiceField = MenuAPI.CreateREPOInputField("AppId Voice", s =>
                {
                    var inputValue = string.IsNullOrEmpty(s) ? null : s.ToLower().Trim();
                    AppIdVoice = inputValue;
                }, scrollView, defaultValue: Instance.AppIdVoice.Value);

                return appIdVoiceField.rectTransform;
            });

            modPage.AddElementToScrollView(scrollView =>
            {
                var player_btn = MenuAPI.CreateREPOButton("", () => { }, scrollView);
                player_btn.enabled = false;

                player_btn.menuButton.doButtonEffect = false;
                player_btn.menuButton.enabled = false;
                var rt = player_btn.rectTransform;
                var hLayout = player_btn.gameObject.AddComponent<HorizontalLayoutGroup>();
                hLayout.childAlignment = TextAnchor.MiddleRight;
                hLayout.spacing = 6f;
                hLayout.padding = new RectOffset(0, 0, 0, 0);
                hLayout.childControlWidth = false;
                hLayout.childControlHeight = false;
                hLayout.childForceExpandWidth = false;
                hLayout.childForceExpandHeight = false;

                var fitter = player_btn.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                var saveBtn = MenuAPI.CreateREPOButton("Save", () =>
                {

                    if(checkAppID(AppIdRealtime , true))
                    {
                        Instance.AppIdRealtime.Value = AppIdRealtime;
                    }
                    if (checkAppID(AppIdVoice, false))
                    {
                        Instance.AppIdVoice.Value = AppIdVoice;
                    }
                    
                }, player_btn.transform);
                var clipboardBtn = MenuAPI.CreateREPOButton("Import from clipboard", () =>
                {
                    string clipboardText = GUIUtility.systemCopyBuffer;
                    if (Guid.TryParseExact(clipboardText ?? "", "D", out _))
                    {
                        var menuButtonPopup = MenuManager.instance.gameObject.AddComponent<MenuButtonPopUp>();

                        menuButtonPopup.option1Event = new UnityEvent();
                        menuButtonPopup.option2Event = new UnityEvent();

                        menuButtonPopup.option1Event.AddListener(new UnityAction(() =>
                        {
                            appIdRealtimeField.inputStringSystem.SetValue(clipboardText, notify: true);
                            if (checkAppID(clipboardText, true))
                            {
                                Instance.AppIdRealtime.Value = clipboardText;
                            }

                        }));

                        menuButtonPopup.option2Event.AddListener(new UnityAction(() =>
                        {
                            appIdVoiceField.inputStringSystem.SetValue(clipboardText, notify: true);

                            if (checkAppID(clipboardText, false))
                            {
                                Instance.AppIdVoice.Value = clipboardText;
                            }

                        }));

                        MenuManager.instance.PagePopUpTwoOptions(menuButtonPopup, "Select the appid type", UnityEngine.Color.yellow, "Please select the AppID type", "Realtime", "Voice", false);
                        return;
                    }
                }, player_btn.transform);

                return player_btn.rectTransform;
            });

            return false;
        }
    }

    private static string GetSteamAuthTicket(out AuthTicket ticket)
    {
        logger.LogInfo("Getting Steam Auth Ticket...");
        // Assuming a default NetIdentity is required, create one. Replace this with the correct identity if needed.
        NetIdentity identity = new NetIdentity(); // Ensure this is the correct way to initialize NetIdentity in your context.
        ticket = SteamUser.GetAuthSessionTicket(identity);
        System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();

        for (int i = 0; i < ticket.Data.Length; i++)
        {
            stringBuilder.AppendFormat("{0:x2}", ticket.Data[i]);
        }

        return stringBuilder.ToString();
    }

    private static void UpdatePhotonSettings()
    {
        ServerSettings serverSettings = FindObjectOfType<ServerSettings>() ?? Resources.Load<ServerSettings>("PhotonServerSettings");

        if (serverSettings != null)
        {
            serverSettings.AppSettings.AppIdRealtime = Instance.AppIdRealtime.Value;
            serverSettings.AppSettings.AppIdVoice = Instance.AppIdVoice.Value;
            

            logger.LogInfo($"AppIdRealtime: {serverSettings.AppSettings.AppIdRealtime}");
            logger.LogInfo($"AppIdVoice: {serverSettings.AppSettings.AppIdVoice}");
            logger.LogInfo($"FixedRegion: {serverSettings.AppSettings.FixedRegion}");

            PhotonNetwork.ConnectUsingSettings();
        }
    }

    private static void UpdateAuthMethod()
    {
        PhotonNetwork.AuthValues = new AuthenticationValues
        {
            UserId = SteamClient.SteamId.ToString(),
            AuthType = (CustomAuthenticationType)255 // Explicitly cast the integer to CustomAuthenticationType
        };
        string value = GetSteamAuthTicket(out steamAuthTicket);
        PhotonNetwork.AuthValues.AddAuthParameter("ticket", value);

        logger.LogDebug($"SteamAuthTicket: {value}");

    }

    [HarmonyPatch(typeof(NetworkConnect))]
    public static class NetworkConnectPatch
    {
        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        public static void Start_Prefix()
        {
            if (Instance.isActive.Value)
            {
                logger.LogInfo("Updating Photon Settings");
                UpdatePhotonSettings();
            }
        }
    }

    [HarmonyPatch(typeof(DataDirector))]
    public static class DataDirectorPatch
    {
        [HarmonyPatch("PhotonSetAppId")]
        [HarmonyPrefix]
        public static void Start_Prefix()
        {
            if (Instance.isActive.Value)
            {
                logger.LogInfo("Updating Photon Settings");
                UpdatePhotonSettings();
            }
        }
    }

    [HarmonyPatch(typeof(SteamManager))]
    public static class SteamManagerPatch
    {
        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        public static void Awake_Prefix()
        {
            if (Instance.isChangeSteamAppId.Value)
            {
                logger.LogInfo($"Change appid to {480U}");
                SteamClient.Init(480U, true);
            }
        }

        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        public static void Start_Prefix()
        {
            if (Instance.isChangeSteamAppId.Value)
            {
                logger.LogInfo("Doing something in Start");
                UpdateAuthMethod();
            }
        }
        //DataDirector.PhotonSetAppId
        [HarmonyPatch("SendSteamAuthTicket")]
        [HarmonyPrefix]
        public static bool SendSteamAuthTicket_Prefix()
        {
            if (Instance.isActive.Value)
            {
                logger.LogInfo("Updating Auth Method");
                UpdateAuthMethod();
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(SteamManager), "SendSteamAuthTicket")]
        public class SendSteamAuthTicketPatch
        {
            // Token: 0x0600000F RID: 15 RVA: 0x00002B93 File Offset: 0x00000D93
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (Instance.isActive.Value)
                {
                    logger.LogInfo("Updating Photon Settings");
                    UpdatePhotonSettings();
                }
                return false;
            }
        }
    }
}
