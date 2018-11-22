﻿using Barotrauma.Items.Components;
using Barotrauma.SpriteDeformations;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Barotrauma.Particles;
using System.Linq;

namespace Barotrauma
{
    abstract partial class Ragdoll
    {
        public HashSet<SpriteDeformation> SpriteDeformations { get; protected set; } = new HashSet<SpriteDeformation>();

        partial void ImpactProjSpecific(float impact, Body body)
        {
            float volume = Math.Min(impact - 3.0f, 1.0f);

            if (body.UserData is Limb && character.Stun <= 0f)
            {
                Limb limb = (Limb)body.UserData;
                if (impact > 3.0f && limb.LastImpactSoundTime < Timing.TotalTime - Limb.SoundInterval)
                {
                    limb.LastImpactSoundTime = (float)Timing.TotalTime;
                    if (!string.IsNullOrWhiteSpace(limb.HitSoundTag))
                    {
                        SoundPlayer.PlaySound(limb.HitSoundTag, volume, impact * 100.0f, limb.WorldPosition, character.CurrentHull);
                    }
                    foreach (WearableSprite wearable in limb.WearingItems)
                    {
                        if (limb.type == wearable.Limb && !string.IsNullOrWhiteSpace(wearable.Sound))
                        {
                            SoundPlayer.PlaySound(wearable.Sound, volume, impact * 100.0f, limb.WorldPosition, character.CurrentHull);
                        }
                    }
                }
            }
            else if (body.UserData is Limb || body == Collider.FarseerBody)
            {
                if (!character.IsRemotePlayer || GameMain.Server != null)
                {
                    if (impact > ImpactTolerance)
                    {
                        SoundPlayer.PlayDamageSound("LimbBlunt", strongestImpact, Collider);
                    }
                }
            }
            if (Character.Controlled == character)
            {
                GameMain.GameScreen.Cam.Shake = Math.Min(Math.Max(strongestImpact, GameMain.GameScreen.Cam.Shake), 3.0f);
            }
        }

        partial void Splash(Limb limb, Hull limbHull)
        {
            //create a splash particle
            for (int i = 0; i < MathHelper.Clamp(Math.Abs(limb.LinearVelocity.Y), 1.0f, 5.0f); i++)
            {
                var splash = GameMain.ParticleManager.CreateParticle("watersplash",
                    new Vector2(limb.WorldPosition.X, limbHull.WorldSurface),
                    new Vector2(0.0f, Math.Abs(-limb.LinearVelocity.Y * 20.0f)) + Rand.Vector(Math.Abs(limb.LinearVelocity.Y * 10)),
                    Rand.Range(0.0f, MathHelper.TwoPi), limbHull);

                if (splash != null)
                {
                    splash.Size *= MathHelper.Clamp(Math.Abs(limb.LinearVelocity.Y) * 0.1f, 1.0f, 2.0f);
                }
            }

            GameMain.ParticleManager.CreateParticle("bubbles",
                new Vector2(limb.WorldPosition.X, limbHull.WorldSurface),
                limb.LinearVelocity * 0.001f,
                0.0f, limbHull);

            //if the Character dropped into water, create a wave
            if (limb.LinearVelocity.Y < 0.0f)
            {
                if (splashSoundTimer <= 0.0f)
                {
                    SoundPlayer.PlaySplashSound(limb.WorldPosition, Math.Abs(limb.LinearVelocity.Y) + Rand.Range(-5.0f, 0.0f));
                    splashSoundTimer = 0.5f;
                }

                //+ some extra bubbles to follow the character underwater
                GameMain.ParticleManager.CreateParticle("bubbles",
                    new Vector2(limb.WorldPosition.X, limbHull.WorldSurface),
                    limb.LinearVelocity * 10.0f,
                    0.0f, limbHull);
            }
        }

        partial void SetupDrawOrder()
        {
            //make sure every character gets drawn at a distinct "layer" 
            //(instead of having some of the limbs appear behind and some in front of other characters)
            float startDepth = 0.1f;
            float increment = 0.001f;
            foreach (Character otherCharacter in Character.CharacterList)
            {
                if (otherCharacter == character) continue;
                startDepth += increment;
            }
            //make sure each limb has a distinct depth value 
            List<Limb> depthSortedLimbs = Limbs.OrderBy(l => l.ActiveSprite == null ? 0.0f : l.ActiveSprite.Depth).ToList();
            foreach (Limb limb in Limbs)
            {
                if (limb.ActiveSprite != null)
                    limb.ActiveSprite.Depth = startDepth + depthSortedLimbs.IndexOf(limb) * 0.00001f;
            }
            depthSortedLimbs.Reverse();
            inversedLimbDrawOrder = depthSortedLimbs.ToArray();
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            LimbJoints.ForEach(j => j.UpdateDeformations(deltaTime));
            SpriteDeformations.ForEach(sd => sd.Update(deltaTime));
        }

        partial void FlipProjSpecific()
        {
            foreach (Limb limb in Limbs)
            {
                if (limb == null || limb.IsSevered || limb.ActiveSprite == null) continue;

                Vector2 spriteOrigin = limb.ActiveSprite.Origin;
                spriteOrigin.X = limb.ActiveSprite.SourceRect.Width - spriteOrigin.X;
                limb.ActiveSprite.Origin = spriteOrigin;                
            }
        }

        partial void SeverLimbJointProjSpecific(LimbJoint limbJoint)
        {
            foreach (Limb limb in new Limb[] { limbJoint.LimbA, limbJoint.LimbB })
            {
                float gibParticleAmount = MathHelper.Clamp(limb.Mass / character.AnimController.Mass, 0.1f, 1.0f);
                foreach (ParticleEmitter emitter in character.GibEmitters)
                {
                    if (inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) continue;
                    if (!inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) continue;

                    emitter.Emit(1.0f, limb.WorldPosition, character.CurrentHull, amountMultiplier: gibParticleAmount);
                }

                if (!string.IsNullOrEmpty(character.BloodDecalName))
                {
                    character.CurrentHull?.AddDecal(character.BloodDecalName, limb.WorldPosition, MathHelper.Clamp(limb.Mass, 0.5f, 2.0f));
                }
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            if (simplePhysicsEnabled) return;

            Collider.UpdateDrawPosition();

            if (Limbs == null)
            {
                DebugConsole.ThrowError("Failed to draw a ragdoll, limbs have been removed. Character: \"" + character.Name + "\", removed: " + character.Removed + "\n" + Environment.StackTrace);
                GameAnalyticsManager.AddErrorEventOnce("Ragdoll.Draw:LimbsRemoved", 
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Failed to draw a ragdoll, limbs have been removed. Character: \"" + character.Name + "\", removed: " + character.Removed + "\n" + Environment.StackTrace);
                return;
            }

            //foreach (Limb limb in Limbs)
            //{
            //    limb.Draw(spriteBatch, cam);
            //}

            for (int i = 0; i < limbs.Length; i++)
            {
                inversedLimbDrawOrder[i].Draw(spriteBatch, cam);
            }
            LimbJoints.ForEach(j => j.Draw(spriteBatch));
        }

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            if (!GameMain.DebugDraw || !character.Enabled) return;
            if (simplePhysicsEnabled) return;

            foreach (Limb limb in Limbs)
            {
                if (limb.PullJointEnabled)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(limb.PullJointWorldAnchorA);
                    if (currentHull?.Submarine != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), Color.Red, true, 0.01f);
                }

                limb.body.DebugDraw(spriteBatch, inWater ? Color.Cyan : Color.White);
            }

            Collider.DebugDraw(spriteBatch, frozen ? Color.Red : (inWater ? Color.SkyBlue : Color.Gray));
            GUI.Font.DrawString(spriteBatch, Collider.LinearVelocity.X.ToString(), new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y), Color.Orange);

            foreach (RevoluteJoint joint in LimbJoints)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorA);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);

                pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.body.TargetPosition != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits((Vector2)limb.body.TargetPosition);
                    if (currentHull?.Submarine != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 10, (int)pos.Y - 10, 20, 20), Color.Cyan, false, 0.01f);
                    GUI.DrawLine(spriteBatch, pos, new Vector2(limb.WorldPosition.X, -limb.WorldPosition.Y), Color.Cyan);
                }
            }

            if (this is HumanoidAnimController humanoid)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(humanoid.RightHandIKPos);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 4, 4), Color.Green, true);
                pos = ConvertUnits.ToDisplayUnits(humanoid.LeftHandIKPos);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 4, 4), Color.Green, true);
            }

            if (outsideCollisionBlocker.Enabled && currentHull?.Submarine != null)
            {
                var edgeShape = outsideCollisionBlocker.FixtureList[0].Shape as FarseerPhysics.Collision.Shapes.EdgeShape;
                Vector2 startPos = ConvertUnits.ToDisplayUnits(outsideCollisionBlocker.GetWorldPoint(edgeShape.Vertex1)) + currentHull.Submarine.Position;
                Vector2 endPos = ConvertUnits.ToDisplayUnits(outsideCollisionBlocker.GetWorldPoint(edgeShape.Vertex2)) + currentHull.Submarine.Position;                
                startPos.Y = -startPos.Y;
                endPos.Y = -endPos.Y;
                GUI.DrawLine(spriteBatch, startPos, endPos, Color.Gray, 0, 5);
            }

            if (character.MemState.Count > 1)
            {
                Vector2 prevPos = ConvertUnits.ToDisplayUnits(character.MemState[0].Position);
                if (currentHull?.Submarine != null) prevPos += currentHull.Submarine.DrawPosition;
                prevPos.Y = -prevPos.Y;

                for (int i = 1; i < character.MemState.Count; i++)
                {
                    Vector2 currPos = ConvertUnits.ToDisplayUnits(character.MemState[i].Position);
                    if (currentHull?.Submarine != null) currPos += currentHull.Submarine.DrawPosition;
                    currPos.Y = -currPos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)currPos.X - 3, (int)currPos.Y - 3, 6, 6), Color.Cyan * 0.6f, true, 0.01f);
                    GUI.DrawLine(spriteBatch, prevPos, currPos, Color.Cyan * 0.6f, 0, 3);

                    prevPos = currPos;
                }
            }

            if (ignorePlatforms)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y),
                    new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y + 50),
                    Color.Orange, 0, 5);
            }
        }
    }
}
