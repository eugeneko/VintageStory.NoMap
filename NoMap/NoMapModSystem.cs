using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace NoMap
{
    [ProtoContract]
    public class ConfigSync
    {
        [ProtoMember(1)]
        public bool Enabled;
    }

    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        public static bool IsMapDisabled()
        {
            return NoMapModSystem.ClientInstance?.IsMapDisabled ?? false;
        }

        public static bool IsUiTogglePending()
        {
            return NoMapModSystem.ClientInstance?.UiTogglePending ?? false;
        }

        public static void ClearUiTogglePending()
        {
            if (NoMapModSystem.ClientInstance is not null)
                NoMapModSystem.ClientInstance.UiTogglePending = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiElementMap), nameof(GuiElementMap.RenderInteractiveElements))]
        public static bool RenderInteractiveElements(GuiElementMap __instance)
        {
            if (!IsMapDisabled())
                return true;

            __instance.ZoomAdd(-0.25f, 0.5f, 0.5f);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GuiDialogWorldMap), "GetMinimapPosition")]
        public static bool GetMinimapPosition(GuiDialogWorldMap __instance, ref EnumDialogArea __result, ref double offsetX, ref double offsetY)
        {
            if (!IsMapDisabled())
                return true;

            __result = EnumDialogArea.LeftTop;
            offsetX = -350;
            offsetY = -350;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HudElementCoordinates), nameof(HudElementCoordinates.OnGuiOpened))]
        public static bool OnGuiOpened(HudElementCoordinates __instance)
        {
            if (!IsMapDisabled())
                return true;

            __instance.TryClose();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HudElementCoordinates), "Every250ms")]
        public static bool Every250ms(HudElementCoordinates __instance, float dt)
        {
            if (!IsMapDisabled())
                return true;

            __instance.TryClose();
            return false;
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(WorldMapManager), "OnClientTick")]
        public static bool OnClientTick(WorldMapManager __instance, float dt)
        {
            if (IsUiTogglePending())
            {
                __instance.ToggleMap(EnumDialogType.HUD);
                __instance.ToggleMap(EnumDialogType.HUD);
                ClearUiTogglePending();
            }

            if (!IsMapDisabled())
                return true;

            if (__instance.worldMapDlg == null || !__instance.worldMapDlg.IsOpened() || __instance.worldMapDlg.DialogType != EnumDialogType.HUD)
                __instance.ToggleMap(EnumDialogType.HUD);
            return true;
        }

    }

    public class NoMapModSystem : ModSystem
    {
        public static NoMapModSystem? ClientInstance;
        public bool IsMapDisabled = false;
        public bool UiTogglePending = false;

        private const string _configKey = "nomap_enabled";
        private IServerNetworkChannel? _serverChannel;
        private IClientNetworkChannel? _clientChannel;

        public override void StartServerSide(ICoreServerAPI api)
        {
            _serverChannel = api.Network
                .RegisterChannel("nomap")
                .RegisterMessageType<ConfigSync>();

            if (!api.World.Config.HasAttribute(_configKey))
            {
                api.World.Config.SetBool(_configKey, false);
            }

            api.ChatCommands
                .Create("nomaptoggle")
                .WithDescription("Toggle NoMap mod.")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(args =>
                {
                    bool enabled = api.World.Config.GetBool(_configKey);
                    enabled = !enabled;

                    api.World.Config.SetBool(_configKey, enabled);
                    _serverChannel.BroadcastPacket(new ConfigSync { Enabled = enabled });

                    return TextCommandResult.Success($"NoMap mod is now {(enabled ? "enabled" : "disabled")}.");
                });
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientInstance = this;

            var harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();

            _clientChannel = api.Network
                .RegisterChannel("nomap")
                .RegisterMessageType<ConfigSync>();
            _clientChannel.SetMessageHandler<ConfigSync>(msg =>
            {
                //api.ShowChatMessage($"NoMap mod: ConfigSync message received: {msg.Enabled}");
                UpdateStatus(msg.Enabled);
            });

            bool isMapDisabled = api.World.Config.GetBool(_configKey);
            UpdateStatus(isMapDisabled);
        }

        private void UpdateStatus(bool isMapDisabled)
        {
            if (IsMapDisabled != isMapDisabled)
            {
                IsMapDisabled = isMapDisabled;
                UiTogglePending = true;
            }
        }

        public override void Dispose()
        {
            ClientInstance = null;
        }
    }
}
