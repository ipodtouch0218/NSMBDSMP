using NSMB.Extensions;
using Quantum;
using UnityEngine;

public class GoombaAnimator : MonoBehaviour {

    //---Serialized Variables
    [SerializeField] private QuantumEntityView entity;
    [SerializeField] private SpriteRenderer sRenderer;
    [SerializeField] private Sprite deadSprite;
    [SerializeField] private GameObject specialKillParticle;
    [SerializeField] private LegacyAnimateSpriteRenderer legacyAnimation;
    [SerializeField] private AudioSource sfx;

    public void OnValidate() {
        this.SetIfNull(ref entity);
        this.SetIfNull(ref sRenderer, UnityExtensions.GetComponentType.Children);
        this.SetIfNull(ref legacyAnimation, UnityExtensions.GetComponentType.Children);
        this.SetIfNull(ref sfx);
    }

    public void Start() {
        QuantumCallback.Subscribe<CallbackUpdateView>(this, OnUpdateView);
        QuantumEvent.Subscribe<EventEnemyKilled>(this, OnEnemyKilled);
        QuantumEvent.Subscribe<EventPlayComboSound>(this, OnPlayComboSound);
    }

    private void OnUpdateView(CallbackUpdateView view) {
        QuantumGame game = view.Game;
        Frame f = game.Frames.Predicted;

        var enemy = f.Get<Enemy>(entity.EntityRef);
        var goomba = f.Get<Goomba>(entity.EntityRef);

        sRenderer.enabled = enemy.IsActive;
        legacyAnimation.enabled = enemy.IsAlive;
        sRenderer.flipX = enemy.FacingRight;

        if (enemy.IsDead) {
            if (goomba.DeathAnimationFrames > 0) {
                // Stomped
                sRenderer.sprite = deadSprite;
            } else {
                // Special killed
                transform.rotation *= Quaternion.Euler(0, 0, 400f * (enemy.FacingRight ? -1 : 1) * Time.deltaTime);
            }
        } else {
            transform.rotation = Quaternion.identity;
        }
    }

    private void OnPlayComboSound(EventPlayComboSound e) {
        if (e.Entity != entity.EntityRef) {
            return;
        }

        sfx.PlayOneShot(QuantumUtils.GetComboSoundEffect(e.Combo));
    }

    private void OnEnemyKilled(EventEnemyKilled e) {
        if (e.Enemy != entity.EntityRef) {
            return;
        }

        if (e.IsSpecialKill) {
            if (e.Frame.TryGet(e.Killer, out MarioPlayer mario) && mario.IsGroundpoundActive) {
                Instantiate(specialKillParticle, transform.position + Vector3.up * 0.2f, Quaternion.identity);
            }
        } else {
            // Play death sound effect
            // sfx.PlayOneShot(SoundEffect.Enemy_Generic_Stomp);
        }
    }
}
