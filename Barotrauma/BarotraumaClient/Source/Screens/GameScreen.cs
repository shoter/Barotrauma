using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using FarseerPhysics;
using System.Diagnostics;

namespace Barotrauma
{
    partial class GameScreen : Screen
    {
        private RenderTarget2D renderTargetBackground;
        private RenderTarget2D renderTarget;
        private RenderTarget2D renderTargetWater;
        private RenderTarget2D renderTargetFinal;

        private Effect damageEffect;
        private Effect postProcessEffect;

        private Texture2D damageStencil;       
        private Texture2D distortTexture;

        public Effect PostProcessEffect
        {
            get { return postProcessEffect; }            
        }

        public GameScreen(GraphicsDevice graphics, ContentManager content)
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));

            CreateRenderTargets(graphics);
            GameMain.Instance.OnResolutionChanged += () =>
            {
                CreateRenderTargets(graphics);
            };

#if LINUX || OSX
            var blurEffect = content.Load<Effect>("Effects/blurshader_opengl");
            damageEffect = content.Load<Effect>("Effects/damageshader_opengl");
#else
            var blurEffect = content.Load<Effect>("Effects/blurshader");
            damageEffect = content.Load<Effect>("Effects/damageshader");
            postProcessEffect = content.Load<Effect>("Effects/postprocess");
#endif

            damageStencil = TextureLoader.FromFile("Content/Map/walldamage.png");
            damageEffect.Parameters["xStencil"].SetValue(damageStencil);
            damageEffect.Parameters["aMultiplier"].SetValue(50.0f);
            damageEffect.Parameters["cMultiplier"].SetValue(200.0f);
            
            distortTexture = TextureLoader.FromFile("Content/Effects/distortnormals.png");
            postProcessEffect.Parameters["xDistortTexture"].SetValue(distortTexture);
        }

        private void CreateRenderTargets(GraphicsDevice graphics)
        {
            renderTarget?.Dispose();
            renderTargetBackground?.Dispose();
            renderTargetWater?.Dispose();
            renderTargetFinal?.Dispose();
            renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            renderTargetBackground = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetWater = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetFinal = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight, false, SurfaceFormat.Color, DepthFormat.None);
        }

        public override void AddToGUIUpdateList()
        {
            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.AddToGUIUpdateList();
            }

            if (GameMain.GameSession != null) GameMain.GameSession.AddToGUIUpdateList();

            Character.AddAllToGUIUpdateList();
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform(true);
            Submarine.CullEntities(cam);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            DrawMap(graphics, spriteBatch, deltaTime);

            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("DrawMap", sw.ElapsedTicks);
            sw.Restart();

            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, GameMain.ScissorTestEnable);
            
            if (Character.Controlled != null && cam != null) Character.Controlled.DrawHUD(spriteBatch, cam);

            if (GameMain.GameSession != null) GameMain.GameSession.Draw(spriteBatch);

            if (Character.Controlled == null && !GUI.DisableHUD)
            {
                for (int i = 0; i < Submarine.MainSubs.Length; i++)
                {
                    if (Submarine.MainSubs[i] == null) continue;
                    if (Level.Loaded != null && Submarine.MainSubs[i].WorldPosition.Y < Level.MaxEntityDepth) continue;
                    
                    Color indicatorColor = i == 0 ? Color.LightBlue * 0.5f : Color.Red * 0.5f;
                    GUI.DrawIndicator(
                        spriteBatch, Submarine.MainSubs[i].WorldPosition, cam, 
                        Math.Max(Submarine.MainSub.Borders.Width, Submarine.MainSub.Borders.Height), 
                        GUI.SubmarineIcon, indicatorColor); 
                }
            }

            GUI.Draw(cam, spriteBatch);

            spriteBatch.End();

            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("DrawHUD", sw.ElapsedTicks);
            sw.Restart();
        }

        public void DrawMap(GraphicsDevice graphics, SpriteBatch spriteBatch, double deltaTime)
        {
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.UpdateTransform();
            }

            GameMain.ParticleManager.UpdateTransforms();

            GameMain.LightManager.ObstructVision = Character.Controlled != null && Character.Controlled.ObstructVision;

            GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam);
            if (Character.Controlled != null)
            {
                GameMain.LightManager.UpdateObstructVision(graphics, spriteBatch, cam, Character.Controlled.CursorWorldPosition);
            }

            graphics.SetRenderTarget(renderTargetBackground);
            if (Level.Loaded == null)
            {
                graphics.Clear(new Color(11, 18, 26, 255));
            }
            else
            {
                //graphics.Clear(new Color(255, 255, 255, 255));
                Level.Loaded.DrawBack(graphics, spriteBatch, cam);
            }

			//draw alpha blended particles that are in water and behind subs
#if LINUX || OSX
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.None, null, null, cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
#endif
			GameMain.ParticleManager.Draw(spriteBatch, true, false, Particles.ParticleBlendState.AlphaBlend);
			spriteBatch.End();
            
            //draw additive particles that are in water and behind subs
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.None, null, null, cam.Transform);
            GameMain.ParticleManager.Draw(spriteBatch, true, false, Particles.ParticleBlendState.Additive);
            spriteBatch.End();
            //Draw resizeable background structures (= background walls) and wall background sprites 
            //(= the background texture that's revealed when a wall is destroyed) into the background render target
            //These will be visible through the LOS effect.
            //Could be drawn with one Submarine.DrawBack call, but we can avoid sorting by depth by doing it like this.
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
            Submarine.DrawBack(spriteBatch, false, s => s is Structure && s.ResizeVertical && s.ResizeHorizontal);
            Submarine.DrawBack(spriteBatch, false, s => s is Structure && !(s.ResizeVertical && s.ResizeHorizontal) && ((Structure)s).Prefab.BackgroundSprite != null);
            spriteBatch.End();

            //Start drawing to the normal render target (stuff that can't be seen through the LOS effect)
            graphics.SetRenderTarget(renderTarget);
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, null);
            spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
            spriteBatch.End();
            //Draw the rest of the structures, characters and front structures
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
            Submarine.DrawBack(spriteBatch, false, s => !(s is Structure) || !(s.ResizeVertical && s.ResizeHorizontal));
            foreach (Character c in Character.CharacterList) c.Draw(spriteBatch, Cam);
            Submarine.DrawFront(spriteBatch, false, null);
            spriteBatch.End();
            
			//draw the rendertarget and particles that are only supposed to be drawn in water into renderTargetWater
			graphics.SetRenderTarget(renderTargetWater);

			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
            spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);// waterColor);
			spriteBatch.End();

			//draw alpha blended particles that are inside a sub
#if LINUX || OSX
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#endif
			GameMain.ParticleManager.Draw(spriteBatch, true, true, Particles.ParticleBlendState.AlphaBlend);
			spriteBatch.End();

			graphics.SetRenderTarget(renderTarget);

			//draw alpha blended particles that are not in water
#if LINUX || OSX
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#endif
			GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.AlphaBlend);
			spriteBatch.End();

			//draw additive particles that are not in water
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.None, null, null, cam.Transform);
			GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.Additive);
			spriteBatch.End();
            
            graphics.DepthStencilState = DepthStencilState.DepthRead;
            graphics.SetRenderTarget(renderTargetFinal);
            
            WaterRenderer.Instance.ResetBuffers();
            Hull.UpdateVertices(graphics, cam, WaterRenderer.Instance);			
            WaterRenderer.Instance.RenderWater(spriteBatch, renderTargetWater, cam);
            WaterRenderer.Instance.RenderAir(graphics, cam, renderTarget, Cam.ShaderTransform);
            graphics.DepthStencilState = DepthStencilState.None;

            spriteBatch.Begin(SpriteSortMode.Immediate,
				BlendState.NonPremultiplied, SamplerState.LinearWrap,
				null, null,
				damageEffect,
				cam.Transform);
			Submarine.DrawDamageable(spriteBatch, damageEffect, false);
			spriteBatch.End();

			//draw additive particles that are inside a sub
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.Default, null, null, cam.Transform);
			GameMain.ParticleManager.Draw(spriteBatch, true, true, Particles.ParticleBlendState.Additive);
			spriteBatch.End();
			if (GameMain.LightManager.LightingEnabled)
			{
				spriteBatch.Begin(SpriteSortMode.Deferred, Lights.CustomBlendStates.Multiplicative, null, DepthStencilState.None, null, null, null);
				spriteBatch.Draw(GameMain.LightManager.LightMap, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
				spriteBatch.End();
			}

			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.None, null, null, cam.Transform);
			foreach (Character c in Character.CharacterList) c.DrawFront(spriteBatch, cam);

			if (Level.Loaded != null) Level.Loaded.DrawFront(spriteBatch, cam);
            if (GameMain.DebugDraw && GameMain.GameSession?.EventManager != null)
            {
                GameMain.GameSession.EventManager.DebugDraw(spriteBatch);
            }

            spriteBatch.End();

            if (GameMain.LightManager.LosEnabled && GameMain.LightManager.LosMode != LosMode.None && Character.Controlled != null)
            {
                GameMain.LightManager.LosEffect.CurrentTechnique = GameMain.LightManager.LosEffect.Techniques["LosShader"];

                GameMain.LightManager.LosEffect.Parameters["xTexture"].SetValue(renderTargetBackground);
                GameMain.LightManager.LosEffect.Parameters["xLosTexture"].SetValue(GameMain.LightManager.LosTexture);

                Color losColor;
                if (GameMain.LightManager.LosMode == LosMode.Transparent)
                {
                    //convert the los color to HLS and make sure the luminance of the color is always the same
                    //as the luminance of the ambient light color
                    float r = Character.Controlled?.CharacterHealth == null ?
                        0.0f : Math.Min(Character.Controlled.CharacterHealth.DamageOverlayTimer * 0.5f, 0.5f);
                    Vector3 ambientLightHls = GameMain.LightManager.AmbientLight.RgbToHLS();
                    Vector3 losColorHls = Color.Lerp(GameMain.LightManager.AmbientLight, Color.Red, r).RgbToHLS();
                    losColorHls.Y = ambientLightHls.Y;
                    losColor = ToolBox.HLSToRGB(losColorHls);
                }
                else
                {
                    losColor = Color.Black;
                }

                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, null, null, GameMain.LightManager.LosEffect, null);
                spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, spriteBatch.GraphicsDevice.Viewport.Width, spriteBatch.GraphicsDevice.Viewport.Height), losColor);
                spriteBatch.End();
            }
            graphics.SetRenderTarget(null);

            float BlurStrength = 0.0f;
            float DistortStrength = 0.0f;
            Vector3 chromaticAberrationStrength = GameMain.Config.ChromaticAberrationEnabled ?
                new Vector3(-0.02f, -0.01f, 0.0f) : Vector3.Zero;

            if (Character.Controlled != null)
            {
                BlurStrength = Character.Controlled.BlurStrength * 0.005f;
                DistortStrength = Character.Controlled.DistortStrength;
                chromaticAberrationStrength -= Vector3.One * Character.Controlled.RadialDistortStrength;
                chromaticAberrationStrength += new Vector3(-0.03f, -0.015f, 0.0f) * Character.Controlled.ChromaticAberrationStrength;
            }
            else
            {
                BlurStrength = 0.0f;
                DistortStrength = 0.0f;
            }

            string postProcessTechnique = "";
            if (BlurStrength > 0.0f)
            {
                postProcessTechnique += "Blur";
                postProcessEffect.Parameters["blurDistance"].SetValue(BlurStrength);
            }
            if (chromaticAberrationStrength != Vector3.Zero)
            {
                postProcessTechnique += "ChromaticAberration";
                postProcessEffect.Parameters["chromaticAberrationStrength"].SetValue(chromaticAberrationStrength);
            }
            if (DistortStrength > 0.0f)
            {
                postProcessTechnique += "Distort";
                postProcessEffect.Parameters["distortScale"].SetValue(Vector2.One * DistortStrength);
                postProcessEffect.Parameters["distortUvOffset"].SetValue(WaterRenderer.Instance.WavePos * 0.001f);
                postProcessEffect.Parameters["xTexture"].SetValue(renderTargetFinal);
            }
            
            if (string.IsNullOrEmpty(postProcessTechnique))
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None);
            }
            else
            {
                postProcessEffect.CurrentTechnique = postProcessEffect.Techniques[postProcessTechnique];
                postProcessEffect.CurrentTechnique.Passes[0].Apply();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, effect: postProcessEffect);
            }
            spriteBatch.Draw(DistortStrength > 0.0f ? distortTexture : renderTargetFinal, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
            spriteBatch.End();            
        }
    }
}
