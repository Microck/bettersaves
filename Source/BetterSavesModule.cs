using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Monocle;

namespace Celeste.Mod.BetterSaves;

public sealed class BetterSavesModule : EverestModule {
    public static BetterSavesModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(BetterSavesModuleSettings);
    public static BetterSavesModuleSettings Settings {
        get {
            BetterSavesModuleSettings settings = (BetterSavesModuleSettings) Instance._Settings;
            settings.Normalize();
            return settings;
        }
    }

    public BetterSavesModule() {
        Instance = this;
        Logger.SetLogLevel(nameof(BetterSavesModule), LogLevel.Info);
    }

    public override void Load() {
        Everest.Events.MainMenu.OnCreateButtons += OnCreateMainMenuButtons;
        On.Celeste.OuiMainMenu.OnBegin += OnMainMenuBegin;
        On.Celeste.MainMenuClimb.Confirm += OnMainMenuClimbConfirm;
    }

    public override void Unload() {
        On.Celeste.MainMenuClimb.Confirm -= OnMainMenuClimbConfirm;
        On.Celeste.OuiMainMenu.OnBegin -= OnMainMenuBegin;
        Everest.Events.MainMenu.OnCreateButtons -= OnCreateMainMenuButtons;
    }

    private static void OnCreateMainMenuButtons(OuiMainMenu menu, List<MenuButton> buttons) {
        MenuButton? climbButton = buttons.Find(button => button is MainMenuClimb);
        if (climbButton == null) {
            Logger.Log(LogLevel.Warn, nameof(BetterSavesModule), "Could not find the native Climb button to replace.");
            return;
        }

        climbButton.OnConfirm = () => {
            Audio.Play(SFX.ui_main_button_select);
            Audio.Play(SFX.ui_main_whoosh_large_in);
            menu.Overworld.Goto<OuiBetterSaves>();
        };
    }

    private static void OnMainMenuClimbConfirm(On.Celeste.MainMenuClimb.orig_Confirm orig, MainMenuClimb self) {
        if (Engine.Scene is not Overworld overworld) {
            orig(self);
            return;
        }

        OpenBetterSaves(overworld);
    }

    private static void OnMainMenuBegin(On.Celeste.OuiMainMenu.orig_OnBegin orig, OuiMainMenu self) {
        if (Engine.Scene is not Overworld overworld) {
            orig(self);
            return;
        }

        OpenBetterSaves(overworld);
    }

    private static void OpenBetterSaves(Overworld overworld) {
        Audio.Play(SFX.ui_main_button_select);
        Audio.Play(SFX.ui_main_whoosh_large_in);
        overworld.Goto<OuiBetterSaves>();
    }
}
