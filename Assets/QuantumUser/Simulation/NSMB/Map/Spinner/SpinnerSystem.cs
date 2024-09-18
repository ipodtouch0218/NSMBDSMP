using Photon.Deterministic;
using Quantum;
using Quantum.Collections;
using System;

public unsafe class SpinnerSystem : SystemMainThreadFilter<SpinnerSystem.Filter> {

    public struct Filter {
        public EntityRef Entity;
        public Transform2D* Transform;
        public Spinner* Spinner;
        public MovingPlatform* MovingPlatform;
    }

    public override void OnInit(Frame f) {
        InteractionSystem.RegisterInteraction<Spinner, MarioPlayer>(OnSpinnerMarioPlayerInteraction);
    }

    public override void Update(Frame f, ref Filter filter) {
        var transform = filter.Transform;
        var spinner = filter.Spinner;
        var movingPlatform = filter.MovingPlatform;

        QHashSet<EntityRef> marios = f.ResolveHashSet(spinner->MariosOnPlatform);
        FP target = 1;
        FP targetAngularVelocity = -200;
        if (marios.Count > 0) {
            if (spinner->AngularVelocity == targetAngularVelocity) {
                spinner->AngularVelocity = 0;
            }
            target = 0;
            targetAngularVelocity = -1800;
            spinner->RotationWaitFrames = 0;
        }
        FP newValue = QuantumUtils.MoveTowards(spinner->ArmPosition, target, spinner->ArmMoveSpeed * f.DeltaTime);
        FP change = newValue - spinner->ArmPosition;

        movingPlatform->Velocity = FPVector2.Up * (change * spinner->ArmMoveDistance * f.UpdateRate);
        spinner->ArmPosition = newValue;

        // Rotation...
        spinner->AngularVelocity = QuantumUtils.MoveTowards(spinner->AngularVelocity, targetAngularVelocity, 22);

        if (QuantumUtils.Decrement(ref spinner->RotationWaitFrames)) {
            Func<FP, int> roundingFunction = spinner->AngularVelocity < 0 ? FPMath.CeilToInt : FPMath.FloorToInt;

            int previousRotation = roundingFunction(spinner->Rotation / 90);
            spinner->Rotation += spinner->AngularVelocity * f.DeltaTime;
            spinner->Rotation = ((spinner->Rotation % 360) + 360) % 360;
            int newRotation = roundingFunction(spinner->Rotation / 90);

            if (spinner->AngularVelocity == -200 && newRotation != previousRotation) {
                // Pause
                spinner->Rotation = roundingFunction(spinner->Rotation / 90) * 90;
                spinner->RotationWaitFrames = 30;
            }
        }

        // Clear after since we are a super early system.
        // It will be ready by next frame.
        marios.Clear();
    }

    public static void OnSpinnerMarioPlayerInteraction(Frame f, EntityRef spinnerEntity, EntityRef marioEntity, PhysicsContact contact) {
        if (FPVector2.Dot(contact.Normal, FPVector2.Up) < PhysicsObjectSystem.GroundMaxAngle) {
            return;
        }

        var spinner = f.Unsafe.GetPointer<Spinner>(spinnerEntity);
        QHashSet<EntityRef> mariosSet = f.ResolveHashSet(spinner->MariosOnPlatform);

        mariosSet.Add(marioEntity);
    }
}