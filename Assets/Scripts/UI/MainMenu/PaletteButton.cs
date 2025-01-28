using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class PaletteButton : MonoBehaviour, ISelectHandler {

    //---Public Variables
    public PaletteSet palette;

    //---Serialized Variables
    [SerializeField] private TMP_Text colorNameString;
    [SerializeField] private Image shirt, overalls;

    public void Instantiate(CharacterAsset player) {
        if (palette == null) {
            if (shirt && overalls)
            {
                Destroy(shirt.gameObject);
                Destroy(overalls.gameObject);
            }
            return;
        }

        CharacterSpecificPalette col = palette.GetPaletteForCharacter(player);
        shirt.color = col.shirtColor;
        overalls.color = col.overallsColor;
    }

    public void OnSelect(BaseEventData eventData) {
        colorNameString.text = palette ? palette.Name : GlobalController.Instance.translationManager.GetTranslation("skin.default");
    }

    public void OnPress() {
        colorNameString.text = palette ? palette.Name : GlobalController.Instance.translationManager.GetTranslation("skin.default");
    }
}
