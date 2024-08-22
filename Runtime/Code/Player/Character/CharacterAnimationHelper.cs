﻿using Code.Player.Character.API;
using UnityEngine;

namespace Code.Player.Character {
    [LuauAPI]
    public class CharacterAnimationHelper : MonoBehaviour {
        public enum CharacterAnimationLayer {
            OVERRIDE_1 = 1,
            OVERRIDE_2 = 2,
            OVERRIDE_3 = 3,
            OVERRIDE_4 = 4,
            UPPER_BODY_1 = 5,
        }

        [Header("References")]
        [SerializeField]
        public Animator animator;

        public EntityAnimationEvents events;
        public ParticleSystem sprintVfx;
        public ParticleSystem jumpPoofVfx;
        public ParticleSystem slideVfx;

        [Header("Variables")] 
        public float minAirborneTime = .4f;
        public float particleMaxDistance = 25f;
        public float directionalBlendLerpMod = 8f;
        public float animationSpeedLerpMod = 5;
        [Tooltip("How long in idle before triggering a random reaction animation. 0 = reactions off")]
        public float idleRectionLength = 3;


        private float nextIdleReactionLength = 0;
        private AnimatorOverrideController animatorOverride;
        private CharacterState currentState = CharacterState.Idle;
        private Vector2 currentVelNormalized = Vector2.zero;
        private Vector2 targetVelNormalized;
        private float verticalVel = 0;
        private float currentPlaybackSpeed = 0;
        private bool firstPerson = false;
        private float lastStateTime = 0;
        private float targetPlaybackSpeed = 0;

        private float lastGroundedTime = 0;
        private bool grounded = false;

        private void Awake() {
            if(sprintVfx){
                sprintVfx.Stop();
            }
            if(jumpPoofVfx){
                jumpPoofVfx.Stop();
            }
            if(slideVfx){
                slideVfx.Stop();
            }

            animatorOverride = animator.runtimeAnimatorController as AnimatorOverrideController;
            if(!animatorOverride){
                animatorOverride = new AnimatorOverrideController(animator.runtimeAnimatorController);
            }
            animator.runtimeAnimatorController = animatorOverride;
        }

        private void Start(){
            var offset = Random.Range(0f,1f);
            animator.SetFloat("AnimationOffset", offset);
        }

        public void SetFirstPerson(bool firstPerson) {
            this.firstPerson = firstPerson;
            if (this.firstPerson) {
                animator.SetLayerWeight(0,0);
            } else {
                animator.SetLayerWeight(0,1);
                this.SetState(new (){state = this.currentState});
            }
        }
        
        private void LateUpdate() {
            UpdateAnimationState();
        }

        private void OnEnable() {
            this.animator.Rebind();
            GetRandomReactionLength();

            //Enter default state
            SetState(new CharacterStateData());
        }

        private void OnDisable() {
            if(sprintVfx){
                sprintVfx.Stop();
            }
            if(jumpPoofVfx){
                jumpPoofVfx.Stop();
            }
            if(slideVfx){
                slideVfx.Stop();
            }
        }

        public bool IsInParticleDistance() {
            return (this.transform.position - Camera.main.transform.position).magnitude <= particleMaxDistance;
        }

        private void UpdateAnimationState() {
            if(!enabled){
                return;
            }
            var modifiedTargetPlaybackSpeed = targetPlaybackSpeed;
            //When idle lerp to a standstill
            if (currentState == CharacterState.Idle) {
                targetVelNormalized = Vector2.zero;
                modifiedTargetPlaybackSpeed = 1;
            } else if (currentState == CharacterState.Jumping){
                modifiedTargetPlaybackSpeed = 1;
            }

            float currentMagnitude = currentVelNormalized.magnitude;
            float targetMagnitude = targetVelNormalized.magnitude;

            //RUNNING SPEED
            //Speed up animations based on actual speed vs target speed
            this.currentPlaybackSpeed = Mathf.Lerp(currentPlaybackSpeed, modifiedTargetPlaybackSpeed, animationSpeedLerpMod * Time.deltaTime);
            animator.SetFloat("MovementPlaybackSpeed", modifiedTargetPlaybackSpeed);

            //Blend directional influence
            float blendMod = targetMagnitude > currentMagnitude ? this.directionalBlendLerpMod : this.directionalBlendLerpMod /2f;
            currentVelNormalized = Vector2.MoveTowards(currentVelNormalized, targetVelNormalized, blendMod * Time.deltaTime);
            animator.SetFloat("VelX",  currentVelNormalized.x * Mathf.Clamp01(currentPlaybackSpeed));
            animator.SetFloat("VelY", Mathf.Lerp(animator.GetFloat("VelY"), verticalVel, Time.deltaTime*1.5f));
            animator.SetFloat("VelZ", currentVelNormalized.y * Mathf.Clamp01(currentPlaybackSpeed));
            animator.SetFloat("Speed", targetMagnitude);

            
            if(grounded){
                lastGroundedTime = Time.time;
                animator.SetBool("Airborne", false);
            }else{
                animator.SetBool("Airborne", Time.time - lastGroundedTime > minAirborneTime);
            }

            if(idleRectionLength > 0 && currentState == CharacterState.Idle && Time.time - lastStateTime > nextIdleReactionLength){
                //Idle reaction
                GetRandomReactionLength();
                animator.SetFloat("ReactIndex", (float)UnityEngine.Random.Range(0,3));
                animator.SetTrigger("React");
                lastStateTime= Time.time+idleRectionLength;//Add time so it doesn't trigger a reaction while a reaction is still playing
            }
        }

        private void GetRandomReactionLength() {
                nextIdleReactionLength = this.idleRectionLength + Random.Range(-this.idleRectionLength/2, this.idleRectionLength/2);
        }

        public void SetVelocity(Vector3 localVel) {
            var targetSpeed = 4.4444445f;
            if(currentState == CharacterState.Sprinting){
                targetSpeed = 6.6666667f;
            } else if(currentState == CharacterState.Crouching){
                targetSpeed = 2.1233335f;
            }
            
            this.targetPlaybackSpeed = Mathf.Max(0, 
                new Vector2(localVel.x, localVel.z).magnitude  / 
                targetSpeed);//The speeds the animations were designed for
            targetVelNormalized = new Vector2(localVel.x, localVel.z).normalized;
            verticalVel = Mathf.Clamp(localVel.y, -10,10);
            //print("localVel: " + localVel + " speed: " +  new Vector2(localVel.x, localVel.z).magnitude + " targetPlaybackSpeed: " + this.targetPlaybackSpeed);
        }

        public void SetState(CharacterStateData syncedState) {
            if (!enabled) {
                return;
            }

            var newState = syncedState.state;
            //this.SetVelocity(syncedState.velocity);
            this.grounded = syncedState.grounded;
            animator.SetBool("Grounded", grounded);
            animator.SetBool("Crouching", syncedState.crouching || syncedState.state == CharacterState.Crouching);
            animator.SetBool("Sprinting", !syncedState.crouching && (syncedState.sprinting|| syncedState.state == CharacterState.Sprinting));

            if(sprintVfx){
                if (newState == CharacterState.Sprinting) {
                    if (this.IsInParticleDistance()) {
                        sprintVfx.Play();
                    }
                } else {
                    sprintVfx.Stop();
                }
            }

            if (this.firstPerson) {
                animator.SetLayerWeight(0,0);
            }

            lastStateTime = Time.time;
            currentState = newState;
        }

        public void TriggerJump(){
            animator.SetTrigger("Jump");
        }

        public void PlayAnimationWithWeight(AnimationClip clip, CharacterAnimationLayer layer, float weight) {
            var layerName = "Override" + (int)layer;
            animator.SetLayerWeight(animator.GetLayerIndex(layerName), weight);

            // print("Setting override layer: " + (int)layerLayer);
            int index = (int)layer;

            if (index <= 4) {
                animatorOverride[layerName] = clip;
                animator.SetBool(layerName + "Looping", clip.isLooping);
                animator.SetTrigger(layerName);
                return;
            }

            // Upper body
            if (index <= 8) {
                index -= 4;
                animatorOverride["UpperBody" + index] = clip;
                animator.SetBool("UpperBody" + index + "Looping", clip.isLooping);
                animator.SetTrigger("UpperBody" + index);
                return;
            }
        }

        public void PlayAnimation(AnimationClip clip, CharacterAnimationLayer layer) {
            if (!enabled) {
                return;
            }
            this.PlayAnimationWithWeight(clip, layer, 1);
        }

        public void StopAnimation(CharacterAnimationLayer layer) {
            if(!enabled){
                return;
            }
            animator.SetBool("Override" + (int)layer + "Looping", false);
        }
    }
}
