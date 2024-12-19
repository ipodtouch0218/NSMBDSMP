using NSMB.Translation;
using Quantum;

public class ToggleChangeableRule : ChangeableRule {

    //---Properties
    public override bool CanIncreaseValue => !(bool) value;
    public override bool CanDecreaseValue => (bool) value;

    protected override void IncreaseValueInternal() {
        if (!(bool) value) {
            value = true;
            canvas.PlayCursorSound();
            SendCommand();
        }
    }

    protected override unsafe void DecreaseValueInternal() {
        if ((bool) value) {
            value = false;
            canvas.PlayCursorSound();
            SendCommand();
        }
    }

    private unsafe void SendCommand() {
        CommandChangeRules cmd = new CommandChangeRules {
            EnabledChanges = ruleType,
        };

        switch (ruleType) {
        case CommandChangeRules.Rules.CustomPowerupsEnabled:
            cmd.CustomPowerupsEnabled = (bool) value;
            break;
        case CommandChangeRules.Rules.TeamsEnabled:
            cmd.TeamsEnabled = (bool) value;
            break;
        }

        QuantumGame game = NetworkHandler.Game;
        int slot = game.GetLocalPlayerSlots()[game.GetLocalPlayers().IndexOf(QuantumUtils.GetHostPlayer(game.Frames.Predicted, out _))];
        game.SendCommand(slot, cmd);
    }

    protected override void UpdateLabel() {
        TranslationManager tm = GlobalController.Instance.translationManager;
        if (value is bool boolValue) {
            label.text = labelPrefix + tm.GetTranslation(boolValue ? "ui.generic.on" : "ui.generic.off");
        }
    }
}