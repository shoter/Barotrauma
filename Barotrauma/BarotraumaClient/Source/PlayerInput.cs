﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    public static class PlayerInput
    {
        static MouseState mouseState, oldMouseState;
        static MouseState latestMouseState; //the absolute latest state, do NOT use for player interaction
        static KeyboardState keyboardState, oldKeyboardState;

        static double timeSinceClick;
        static Point lastClickPosition;

        const float DoubleClickDelay = 0.4f;
        const float MaxDoubleClickDistance = 10.0f;

        static bool doubleClicked;

        static bool allowInput;
        static bool wasWindowActive;
        
        public static Vector2 MousePosition
        {
            get { return new Vector2(mouseState.Position.X, mouseState.Position.Y); }
        }

        public static Vector2 LatestMousePosition
        {
            get { return new Vector2(latestMouseState.Position.X, latestMouseState.Position.Y); }
        }

        //public static MouseState GetMouseState
        //{
        //    get { return mouseState; }
        //}
        //public static MouseState GetOldMouseState
        //{
        //    get { return oldMouseState; }
        //}

        public static bool MouseInsideWindow
        {
            get { return new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight).Contains(MousePosition); }
        }

        public static Vector2 MouseSpeed
        {
            get
            {
                return AllowInput ? MousePosition - new Vector2(oldMouseState.X, oldMouseState.Y) : Vector2.Zero;
            }
        }

        private static bool AllowInput
        {
            get { return GameMain.WindowActive && allowInput; }
        }

        public static Vector2 MouseSpeedPerSecond { get; private set; }

        public static KeyboardState GetKeyboardState
        {
            get { return keyboardState; }
        }

        public static KeyboardState GetOldKeyboardState
        {
            get { return oldKeyboardState; }
        }

        public static int ScrollWheelSpeed
        {
            get { return AllowInput ? mouseState.ScrollWheelValue - oldMouseState.ScrollWheelValue : 0; }

        }

        public static bool LeftButtonHeld()
        {
            return AllowInput && mouseState.LeftButton == ButtonState.Pressed;
        }

        public static bool LeftButtonDown()
        {
            return AllowInput &&
                oldMouseState.LeftButton == ButtonState.Released &&
                mouseState.LeftButton == ButtonState.Pressed;
        }

        public static bool LeftButtonReleased()
        {
            return AllowInput && mouseState.LeftButton == ButtonState.Released;
        }


        public static bool LeftButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.LeftButton == ButtonState.Pressed
                && mouseState.LeftButton == ButtonState.Released);
        }

        public static bool RightButtonHeld()
        {
            return AllowInput && mouseState.RightButton == ButtonState.Pressed;
        }

        public static bool RightButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.RightButton == ButtonState.Pressed
                && mouseState.RightButton == ButtonState.Released);
        }

        public static bool MidButtonClicked()
        {
            return (AllowInput &&
                oldMouseState.MiddleButton == ButtonState.Pressed
                && mouseState.MiddleButton == ButtonState.Released);
        }

        public static bool MidButtonHeld()
        {
            return AllowInput && mouseState.MiddleButton == ButtonState.Pressed;
        }

        public static bool DoubleClicked()
        {
            return AllowInput && doubleClicked;
        }

        public static bool KeyHit(InputType inputType)
        {
            return AllowInput && GameMain.Config.KeyBind(inputType).IsHit();
        }

        public static bool KeyDown(InputType inputType)
        {
            return AllowInput && GameMain.Config.KeyBind(inputType).IsDown();
        }

        public static bool KeyUp(InputType inputType)
        {
            return AllowInput && !GameMain.Config.KeyBind(inputType).IsDown();
        }

        public static bool KeyHit(Keys button)
        {
            return (AllowInput && oldKeyboardState.IsKeyDown(button) && keyboardState.IsKeyUp(button));
        }

        public static bool KeyDown(Keys button)
        {
            return (AllowInput && keyboardState.IsKeyDown(button));
        }

        public static bool KeyUp(Keys button)
        {
            return AllowInput && keyboardState.IsKeyUp(button);
        }

        public static void Update(double deltaTime)
        {
            timeSinceClick += deltaTime;

            if (!GameMain.WindowActive)
            {
                wasWindowActive = false;
                return;
            }

            //window was not active during the previous frame -> ignore inputs from this frame
            if (!wasWindowActive)
            {
                wasWindowActive = true;
                allowInput = false;
            }
            else
            {
                allowInput = true;
            }

            oldMouseState = mouseState;
            mouseState = latestMouseState;
            UpdateVariable();

            oldKeyboardState = keyboardState;
            keyboardState = Keyboard.GetState();

            MouseSpeedPerSecond = MouseSpeed / (float)deltaTime;

            doubleClicked = false;
            if (LeftButtonClicked())
            {
                if (timeSinceClick < DoubleClickDelay &&
                    (mouseState.Position - lastClickPosition).ToVector2().Length() < MaxDoubleClickDistance)
                {
                    doubleClicked = true;
                }
                lastClickPosition = mouseState.Position;
                timeSinceClick = 0.0;
            }            
        }

        public static void UpdateVariable()
        {
            //do NOT use this for actual interaction with the game, this is to be used for debugging and rendering ONLY

            latestMouseState = Mouse.GetState();
        }
    }
}
