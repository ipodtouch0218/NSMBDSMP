using NSMB.Extensions;
using NSMB.Utils;
using Quantum;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NSMB.Loading {
    public class LoadingCanvas : MonoBehaviour {

        //---Serialized Variables
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private MarioLoader mario;

        [SerializeField] private Animator animator;
        [SerializeField] private CanvasGroup loadingGroup, readyGroup;
        [SerializeField] private Image readyBackground;

        //---Private Variables
        private bool initialized;
        private Coroutine fadeCoroutine;

        public void OnValidate() {
            this.SetIfNull(ref mario, UnityExtensions.GetComponentType.Children);
        }

        public void Awake() {
            QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
        }

        public void Initialize() {
            initialized = true;

            NetworkUtils.GetCustomProperty(NetworkHandler.Client.LocalPlayer.CustomProperties, Enums.NetPlayerProperties.Character, out int characterIndex);
            var characters = GlobalController.Instance.config.CharacterDatas;
            mario.Initialize(characters[characterIndex % characters.Length]);

            readyGroup.gameObject.SetActive(false);
            gameObject.SetActive(true);

            loadingGroup.alpha = 1;
            readyGroup.alpha = 0;
            readyBackground.color = Color.clear;

            animator.Play("waiting");

            audioSource.volume = 0;
            audioSource.Play();

            if (fadeCoroutine != null) {
                StopCoroutine(fadeCoroutine);
            }

            fadeCoroutine = StartCoroutine(FadeVolume(0.1f, true));

            //audioListener.enabled = true;
        }

        private void OnGameStateChanged(EventGameStateChanged e) {
            if (e.NewState == GameState.Starting) {
                EndLoading();
            }
        }

        public void EndLoading() {

            bool spectator = false;
            // TODO bool spectator = NetworkHandler.Runner.GetLocalPlayerData().IsCurrentlySpectating;
            readyGroup.gameObject.SetActive(true);
            animator.SetTrigger(spectator ? "spectating" : "loaded");

            initialized = false;

            if (fadeCoroutine != null) {
                StopCoroutine(fadeCoroutine);
            }

            fadeCoroutine = StartCoroutine(FadeVolume(0.1f, false));
            //audioListener.enabled = false;
        }

        public void EndAnimation() {
            gameObject.SetActive(false);
            initialized = false;
        }

        private IEnumerator FadeVolume(float fadeTime, bool fadeIn) {
            float currentVolume = audioSource.volume;
            float fadeRate = 1f / fadeTime;

            while (true) {
                currentVolume += fadeRate * Time.deltaTime * (fadeIn ? 1 : -1);

                if (currentVolume < 0 || currentVolume > 1) {
                    audioSource.volume = Mathf.Clamp01(currentVolume);
                    break;
                }

                audioSource.volume = currentVolume;
                yield return null;
            }

            if (!fadeIn) {
                audioSource.Stop();
            }
        }
    }
}
