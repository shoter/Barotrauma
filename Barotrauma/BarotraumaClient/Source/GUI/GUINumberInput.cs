﻿using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class GUINumberInput : GUIComponent
    {
        public enum NumberType
        {
            Int, Float
        }

        public delegate void OnValueChangedHandler(GUINumberInput numberInput);
        public OnValueChangedHandler OnValueChanged;
        
        public GUITextBox TextBox { get; private set; }
        public GUIButton PlusButton { get; private set; }
        public GUIButton MinusButton { get; private set; }

        private NumberType inputType;
        public NumberType InputType
        {
            get { return inputType; }
            set
            {
                inputType = value;
                PlusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                MinusButton.Visible = PlusButton.Visible;
            }
        }

        private float? minValueFloat, maxValueFloat;
        public float? MinValueFloat
        {
            get { return minValueFloat; }
            set
            {
                minValueFloat = value;
                ClampFloatValue();
                PlusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                MinusButton.Visible = PlusButton.Visible;
            }                
        }
        public float? MaxValueFloat
        {
            get { return maxValueFloat; }
            set
            {
                maxValueFloat = value;
                ClampFloatValue();
                PlusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                MinusButton.Visible = PlusButton.Visible;
            }
        }

        private float floatValue;
        public float FloatValue
        {
            get { return floatValue; }
            set
            {
                if (value == floatValue) return;
                floatValue = value;
                ClampFloatValue();
                float newValue = floatValue;
                UpdateText();
                //UpdateText may remove decimals from the value, force to full accuracy
                floatValue = newValue; 
                OnValueChanged?.Invoke(this);
            }
        }

        private int decimalsToDisplay = 1;
        public int DecimalsToDisplay
        {
            get { return decimalsToDisplay; }
            set
            {
                decimalsToDisplay = value;
                UpdateText();
            }
        }

        public int? MinValueInt, MaxValueInt;

        private int intValue;
        public int IntValue
        {
            get { return intValue; }
            set
            {
                if (value == intValue) return;
                intValue = value;
                UpdateText();
                OnValueChanged?.Invoke(this);
            }
        }

        private float pressedTimer;
        private float pressedDelay = 0.5f;
        private bool IsPressedTimerRunning { get { return pressedTimer > 0; } }

        public GUINumberInput(RectTransform rectT, NumberType inputType, string style = "", Alignment textAlignment = Alignment.Center) : base(style, rectT)
        {
            int buttonHeight = Rect.Height / 2;
            int margin = 2;
            Point buttonSize = new Point(buttonHeight - margin, buttonHeight - margin);
            TextBox = new GUITextBox(new RectTransform(new Point(Rect.Width, Rect.Height), rectT), textAlignment: textAlignment, style: style)
            {
                ClampText = false,
                // For some reason the caret in the number inputs is dimmer than it should.
                // It should not be rendered behind anything, as I first suspected.
                // Therefore this hack.
                CaretColor = Color.White
            };
            TextBox.OnTextChanged += TextChanged;
            var buttonArea = new GUIFrame(new RectTransform(new Point(buttonSize.X, buttonSize.Y * 2), rectT, Anchor.CenterRight), style: null);
            PlusButton = new GUIButton(new RectTransform(buttonSize, buttonArea.RectTransform), "+");
            PlusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            PlusButton.OnClicked += (button, data) =>
            {
                if (inputType == NumberType.Int)
                {
                    IntValue++;
                }
                else if (inputType == NumberType.Float)
                {
                    FloatValue += Round();
                }
                return true;
            };
            PlusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    if (inputType == NumberType.Int)
                    {
                        IntValue++;
                    }
                    else if (maxValueFloat.HasValue && minValueFloat.HasValue)
                    {
                        FloatValue += (MaxValueFloat.Value - minValueFloat.Value) / 100.0f;
                    }
                }
                return true;
            };
            PlusButton.Visible = inputType == NumberType.Int;

            MinusButton = new GUIButton(new RectTransform(buttonSize, buttonArea.RectTransform, Anchor.BottomRight), "-");
            MinusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            MinusButton.OnClicked += (button, data) =>
            {
                if (inputType == NumberType.Int)
                {
                    IntValue--;
                }
                else if (inputType == NumberType.Float)
                {
                    FloatValue -= Round();
                }
                return true;
            };
            MinusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    if (inputType == NumberType.Int)
                    {
                        IntValue--;
                    }
                    else if (maxValueFloat.HasValue && minValueFloat.HasValue)
                    {
                        FloatValue -= (MaxValueFloat.Value - minValueFloat.Value) / 100.0f;
                    }
                }
                return true;
            };
            MinusButton.Visible = inputType == NumberType.Int;

            if (inputType == NumberType.Int)
            {
                UpdateText();
                TextBox.OnEnterPressed += (txtBox, txt) =>
                {
                    UpdateText();
                    TextBox.Deselect();
                    return true;
                };
                TextBox.OnDeselected += (txtBox, key) => UpdateText();
            }
            else if (inputType == NumberType.Float)
            {
                UpdateText();
                TextBox.OnDeselected += (txtBox, key) => UpdateText();
                TextBox.OnEnterPressed += (txtBox, txt) =>
                {
                    UpdateText();
                    TextBox.Deselect();
                    return true;
                };
            }
            InputType = inputType;
            switch (InputType)
            {
                case NumberType.Int:
                    TextBox.textFilterFunction = text => new string(text.Where(c => char.IsNumber(c)).ToArray());
                    break;
                case NumberType.Float:
                    TextBox.textFilterFunction = text => new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                    break;
            }
        }

        /// <summary>
        /// Calculates one tent between the range as the increment/decrement.
        /// This value is rounded so that the bigger it is, the less decimals are used (min 0, max 3).
        /// Return value is clamped between 0.1f and 1000.
        /// </summary>
        private float Round()
        {
            if (!maxValueFloat.HasValue || !minValueFloat.HasValue) return 0;
            float tenPercent = MathHelper.Lerp(minValueFloat.Value, maxValueFloat.Value, 0.1f);
            float diff = maxValueFloat.Value - minValueFloat.Value;
            int decimals = (int)MathHelper.Lerp(3, 0, MathUtils.InverseLerp(10, 1000, diff));
            return MathHelper.Clamp((float)Math.Round(tenPercent / 100, decimals) * 100, 0.1f, 1000);
        }

        private bool TextChanged(GUITextBox textBox, string text)
        {
            switch (InputType)
            {
                case NumberType.Int:
                    int newIntValue = IntValue;
                    if (string.IsNullOrWhiteSpace(text) || text == "-")
                    {
                        intValue = 0;
                    }
                    else if (int.TryParse(text, out newIntValue))
                    {
                        intValue = newIntValue;
                    }
                    ClampIntValue();
                    break;
                case NumberType.Float:
                    float newFloatValue = FloatValue;
                    if (string.IsNullOrWhiteSpace(text) || text == "-")
                    {
                        floatValue = 0;
                    }
                    else if (float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out newFloatValue))
                    {
                        floatValue = newFloatValue;
                    }
                    ClampFloatValue();
                    break;
            }
            OnValueChanged?.Invoke(this);
            return true;
        }

        private void ClampFloatValue()
        {
            if (MinValueFloat != null)
            {
                floatValue = Math.Max(floatValue, MinValueFloat.Value);
                MinusButton.Enabled = floatValue > MinValueFloat;
            }
            if (MaxValueFloat != null)
            {
                floatValue = Math.Min(floatValue, MaxValueFloat.Value);
                PlusButton.Enabled = floatValue < MaxValueFloat;
            }
        }

        private void ClampIntValue()
        {
            if (MinValueInt != null)
            {
                intValue = Math.Max(intValue, MinValueInt.Value);
                MinusButton.Enabled = intValue > MinValueInt;
            }
            if (MaxValueInt != null)
            {
                intValue = Math.Min(intValue, MaxValueInt.Value);
                PlusButton.Enabled = intValue < MaxValueInt;
            }
        }

        private void UpdateText()
        {
            switch (InputType)
            {
                case NumberType.Float:
                    TextBox.Text = FloatValue.Format(decimalsToDisplay);
                    break;
                case NumberType.Int:
                    TextBox.Text = IntValue.ToString();
                    break;
            }
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (IsPressedTimerRunning)
            {
                pressedTimer -= deltaTime;
            }
        }
    }
}
