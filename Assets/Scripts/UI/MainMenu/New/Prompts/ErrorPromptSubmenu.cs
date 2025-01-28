using NSMB.Translation;
using NSMB.Utils;
using Photon.Realtime;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace NSMB.UI.MainMenu.Submenus.Prompts {
    public class ErrorPromptSubmenu : PromptSubmenu {

        //---Serialized Variables
        [SerializeField] private TMP_Text headerText, errorText;

        public void OpenWithRealtimeErrorCode(short code) {
            OpenWithString(NetworkUtils.RealtimeErrorCodes.GetValueOrDefault(code, "ui.error.unknown"));
        }

        public void OpenWithRealtimeDisconnectCause(DisconnectCause cause) {

        }

        public void OpenWithString(string str) {
            TranslationManager tm = GlobalController.Instance.translationManager;
            errorText.text = tm.GetTranslation(str);
        }
    }
}