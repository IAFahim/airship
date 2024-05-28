﻿using System;
using Animancer;
using Code.Player.Character.API;
using FishNet;
using UnityEngine;

namespace Code.Player.Character {
    [LuauAPI]
    public class CharacterAnimationHelper : MonoBehaviour {
        [Header("References")]
        [SerializeField]
        public AnimancerComponent worldmodelAnimancer;

        public EntityAnimationEvents events;

        [NonSerialized] public AnimancerLayer rootLayerWorld;
        [NonSerialized] public AnimancerLayer layer1World;
        [NonSerialized] public AnimancerLayer layer2World;
        [NonSerialized] public AnimancerLayer layer3World;
        [NonSerialized] public AnimancerLayer layer4World;

        // public AnimationClip JumpAnimation;
        // public AnimationClip FallAnimation;
        public AnimationClip SlideAnimation;

        public MixerTransition2D moveTransition;
        public MixerTransition2D sprintTransition;
        public MixerTransition2D crouchTransition;

        public ParticleSystem sprintVfx;
        public ParticleSystem jumpPoofVfx;
        public ParticleSystem slideVfx;

        [Header("Variables")] 
        public float defaultFadeDuration = .25f;
        public float quickFadeDuration = .1f;
        public float jumpFadeDuration = .2f;
        public float runAnimSpeedMod = 1;
        public float maxRunAnimSpeed = 3f;
        public float directionalLerpMod = 5;
        public float spineClampAngle = 15;
        public float neckClampAngle = 35;
        public float particleMaxDistance = 25f;
        public float blendSpeed = 8f;

        private MixerState<Vector2> moveStateWorld;
        private MixerState<Vector2> sprintStateWorld;
        private MixerState<Vector2> crouchStateWorld;
        private CharacterState currentState = CharacterState.Idle;
        private Vector2 currentMoveDir = Vector2.zero;
        private Vector2 targetMoveDir;
        private float currentSpeed = 0;
        private bool movementIsDirty = false;
        private bool firstPerson = false;

        private void Awake() {
            worldmodelAnimancer.Playable.ApplyAnimatorIK = true;

            sprintVfx.Stop();
            jumpPoofVfx.Stop();
            slideVfx.Stop();

            // Worldmodel layers
            rootLayerWorld = worldmodelAnimancer.Layers[0];
            rootLayerWorld.SetDebugName("Layer0 (Root)");

            layer1World = worldmodelAnimancer.Layers[1];
            layer1World.DestroyStates();
            layer1World.SetDebugName("Layer1");

            layer2World = worldmodelAnimancer.Layers[2];
            layer2World.SetDebugName("Layer2");
            layer2World.DestroyStates();

            layer3World = worldmodelAnimancer.Layers[3];
            layer3World.SetDebugName("Layer3");
            layer3World.DestroyStates();

            layer4World = worldmodelAnimancer.Layers[4];
            layer4World.SetDebugName("Layer4");
            layer4World.DestroyStates();

            moveStateWorld = (MixerState<Vector2>)worldmodelAnimancer.States.GetOrCreate(moveTransition);
            sprintStateWorld = (MixerState<Vector2>)worldmodelAnimancer.States.GetOrCreate(sprintTransition);
            crouchStateWorld = (MixerState<Vector2>)worldmodelAnimancer.States.GetOrCreate(crouchTransition);

            //Initialize move state
            SetVelocity(Vector3.zero);
            SetState(CharacterState.Idle);
        }

        public void SetFirstPerson(bool firstPerson) {
            this.firstPerson = firstPerson;
            if (this.firstPerson) {
                rootLayerWorld.Weight = 0f;
            } else {
                rootLayerWorld.Weight = 1f;
                this.SetState(this.currentState, true, true);
            }
        }
        
        private void LateUpdate() {
            UpdateAnimationState();
        }

        private void OnEnable() {
            this.worldmodelAnimancer.Animator.Rebind();

            this.SetState(CharacterState.Idle, true);
        }

        private void Start() {
            this.SetState(CharacterState.Idle, true);
        }

        private void OnDisable() {
            this.sprintVfx.Stop();
            this.jumpPoofVfx.Stop();
            this.slideVfx.Stop();
            this.currentState = CharacterState.Idle;
        }

        public bool IsInParticleDistance() {
            return (this.transform.position - Camera.main.transform.position).magnitude <= particleMaxDistance;
        }

        private void UpdateAnimationState() {
            // if (!movementIsDirty) {
            //     return;
            // }
            float moveDeltaMod = (currentState == CharacterState.Sprinting || currentState == CharacterState.Sliding) ? 2 : 1;
            float timeDelta = Time.deltaTime * directionalLerpMod;
            if (InstanceFinder.TimeManager != null) {
                timeDelta = (float)InstanceFinder.TimeManager.TickDelta * directionalLerpMod;
            }
            float magnitude = targetMoveDir.magnitude;
            float speed = magnitude * runAnimSpeedMod;
            
            //When idle lerp to a standstill
            if (currentState == CharacterState.Idle) {
                targetMoveDir = Vector2.zero;
                speed = 1;
            }
            
            //Smoothly adjust animation values
            var newMoveDir = Vector2.Lerp(currentMoveDir, targetMoveDir * moveDeltaMod, timeDelta);
            var newSpeed = Mathf.Lerp(currentSpeed, Mathf.Clamp(speed, 1, maxRunAnimSpeed),
                timeDelta);

            // if (currentMoveDir == newMoveDir && Math.Abs(currentSpeed - newSpeed) < .01) {
            //     movementIsDirty = false;
            //     return;
            // }

            currentMoveDir = newMoveDir;
            currentSpeed = newSpeed;
            
            //Apply values to animator
            if (currentState == CharacterState.Sprinting) {
                //Sprinting
                sprintStateWorld.Parameter = Vector2.MoveTowards(sprintStateWorld.Parameter, currentMoveDir,
                    this.blendSpeed * Time.deltaTime);
                sprintStateWorld.Speed = Mathf.Clamp(currentSpeed, 1, maxRunAnimSpeed);
            } else if (currentState == CharacterState.Crouching) {
                //Crouching
                crouchStateWorld.Parameter = Vector2.MoveTowards(crouchStateWorld.Parameter, currentMoveDir,
                    this.blendSpeed * Time.deltaTime);
                crouchStateWorld.Speed = Mathf.Clamp(currentSpeed, 1, maxRunAnimSpeed);
            } else {
                //Default movement
                moveStateWorld.Parameter = Vector2.MoveTowards(moveStateWorld.Parameter, currentMoveDir,
                    this.blendSpeed * Time.deltaTime);
                moveStateWorld.Speed = Mathf.Clamp(currentSpeed, 1, maxRunAnimSpeed);
            }

            if (currentState == CharacterState.Jumping) {
                moveStateWorld.Speed *= 0.45f;
                sprintStateWorld.Speed *= .45f;
                //TODO: This isn't working? Trying to make the jump animation still work in idle state
                // if(currentMoveDir.magnitude < .05){
                //     moveStateWorld.Parameter = new Vector2(0,1);
                //     sprintStateWorld.Parameter = new Vector2(0,1);
                // }
            }
        }

        public void SetVelocity(Vector3 localVel) {
            movementIsDirty = true;
            targetMoveDir = new Vector2(localVel.x, localVel.z).normalized;
        }

        public void SetState(CharacterState newState, bool force = false, bool noRootLayerFade = false) {
            // if (!worldmodelAnimancer.gameObject.activeInHierarchy) return;

            if (newState == currentState && !force) {
                return;
            }

            movementIsDirty = true;
            if (currentState == CharacterState.Jumping && newState != CharacterState.Jumping) {
                TriggerLand();
            }
            if (newState == CharacterState.Sliding)
            {
                StartSlide();
            } else if(currentState == CharacterState.Sliding)
            {
                StopSlide();
            }
            currentState = newState;

            if (newState == CharacterState.Idle || newState == CharacterState.Running || newState == CharacterState.Jumping) {
                rootLayerWorld.Play(moveStateWorld, noRootLayerFade ? 0f : defaultFadeDuration);
            } else if(newState == CharacterState.Sprinting){
                rootLayerWorld.Play(sprintStateWorld, noRootLayerFade ? 0f : defaultFadeDuration);
            }else if (newState == CharacterState.Jumping) {
                // rootLayer.Play(FallAnimation, defaultFadeDuration);
            } else if (newState == CharacterState.Crouching) {
                rootLayerWorld.Play(crouchStateWorld, noRootLayerFade ? 0f : defaultFadeDuration);
            }

            if (newState == CharacterState.Sprinting) {
                if (this.IsInParticleDistance()) {
                    sprintVfx.Play();
                }
            } else {
                sprintVfx.Stop();
            }

            if (this.firstPerson) {
                rootLayerWorld.Weight = 0f;
            }
        }

        private void StartSlide() {
            layer1World.Play(SlideAnimation, quickFadeDuration);
            if (IsInParticleDistance()) {
                slideVfx.Play();
            }
            events.TriggerBasicEvent(EntityAnimationEventKey.SLIDE_START);
        }

        private void StopSlide() {
            layer1World.StartFade(0, defaultFadeDuration);
            slideVfx.Stop();
            events.TriggerBasicEvent(EntityAnimationEventKey.SLIDE_END);
        }

        public void TriggerJump() {
            // rootOverrideLayer.Play(JumpAnimation, jumpFadeDuration).Events.OnEnd += () => {
            //     rootOverrideLayer.StartFade(0, jumpFadeDuration);
            // };
            events.TriggerBasicEvent(EntityAnimationEventKey.JUMP);
        }

        public void TriggerLand() {
            events.TriggerBasicEvent(EntityAnimationEventKey.LAND);
        }

        public AnimancerLayer GetLayer(int layerIndex){
            switch(layerIndex){
                case 0:
                    return rootLayerWorld;
                case 1:
                    return layer1World;
                case 2:
                    return layer2World;
                case 3:
                    return layer3World;
                case 4:
                    return layer4World;
                default:
                    Debug.LogError("Trying to use layer that doesn't exist: " +layerIndex + ". Layers are 0-4");
                    return null;
            }
        }

        public AnimancerState GetPlayingState(int layerIndex){
            return GetLayer(layerIndex)?.CurrentState;
        }

        public AnimancerState PlayRoot(AnimationClip clip, AnimationClipOptions options){
            return Play(clip, 0, options);
        }

        public AnimancerState PlayRootOneShot(AnimationClip clip){
            return Play(clip, 0, new AnimationClipOptions());
        }

        public AnimancerState PlayOneShot(AnimationClip clip, int layerIndex){
            return Play(clip, layerIndex, new AnimationClipOptions());
        }

        public AnimancerState Play(AnimationClip clip, int layerIndex, AnimationClipOptions options) {
            AnimancerLayer layer = GetLayer(layerIndex);
            
            var previousState = layer.CurrentState;
            var state = layer.Play(clip, options.fadeDuration, options.fadeMode);
            state.Speed = options.playSpeed;
            if(options.autoFadeOut && clip.isLooping == false){
                state.Events.OnEnd = ()=>{
                    if(options.fadeOutToClip != null){
                        layer.Play(options.fadeOutToClip, options.fadeDuration, options.fadeMode);
                    }else if(previousState != null){
                        layer.Play(previousState, options.fadeDuration, options.fadeMode);
                    }
                };
            }
            return state;
        }
    }
}
