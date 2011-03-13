using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Toolkit {
    internal struct FormatInstruction {
        public readonly SpriteFont Font;
        public readonly Color Color;
        public FormatInstruction(SpriteFont font, Color color) {
            Font = font;
            Color = color;
        }
    }

    public interface ICompiledElement {
        void Draw(SpriteBatch spriteBatch, Vector2 offset);
        Vector2 Size { get; }
        Vector2 Position { get; set; }
    }

    internal struct CompiledTextElement : ICompiledElement {
        readonly string text;
        public Vector2 Position { get; set; }
        readonly FormatInstruction formatInstruction;
        private readonly Vector2 size;
        public Vector2 Size { get { return size; } }

        public CompiledTextElement(string text, Vector2 position, FormatInstruction formatInstruction)
            : this() {
            this.text = text;
            Position = position;
            this.formatInstruction = formatInstruction;
            size = formatInstruction.Font.MeasureString(text);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 offset) {
            var origin = new Vector2(0, Size.Y / 2f);
            spriteBatch.DrawString(formatInstruction.Font, text, offset + Position, formatInstruction.Color, 0, origin, Vector2.One, SpriteEffects.None, 0);
        }
    }


    internal struct CompiledImageElement : ICompiledElement {
        readonly Texture2D image;
        public Vector2 Position { get; set; }
        private readonly Vector2 size;
        public Vector2 Size { get { return size; } }
        private Color color;

        public CompiledImageElement(Texture2D image, Color color, Vector2 position)
            : this() {
            this.image = image;
            Position = position;
            this.color = color;
            size = new Vector2(image.Width, image.Height);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 offset) {
            var origin = new Vector2(0, size.Y / 2f);
            spriteBatch.Draw(image, offset + Position, null, color, 0, origin, Vector2.One, SpriteEffects.None, 0);
        }
    }


    public class CompiledMarkup : List<ICompiledElement> {
        public Vector2 Size { get; internal set; }
        public string Text { get; internal set; }
        public void Draw(SpriteBatch spriteBatch, Vector2 position) {
            for (var i = 0; i < this.Count; i++) {
                this[i].Draw(spriteBatch, position);
            }
        }
    }

    public class MarkupTextEngine {
        private readonly Func<string, SpriteFont> fontResolver;
        private readonly Func<string, Texture2D> imageResolver;
        private readonly Func<string, bool> conditionalResolver;

        public MarkupTextEngine(Func<string, SpriteFont> fontResolver, Func<string, Texture2D> imageResolver, Func<string, bool> conditionalResolver) {
            this.fontResolver = fontResolver;
            this.imageResolver = imageResolver;
            this.conditionalResolver = conditionalResolver;
        }

        public void Compile(string text, float width, CompiledMarkup compiledMarkup) {
            var reader = XmlReader.Create(new StringReader(text));
            var position = Vector2.Zero;
            var formatingStack = new Stack<FormatInstruction>();
            var conditionalsStack = new Stack<bool>();
            var lineBuffer = new List<ICompiledElement>();
            float currentLineHeight;
            float currentTotalHeight = 0;
            var maxLineWidth = float.MinValue;
            float currentLineWidth = 0;

            while (reader.Read()) {
                switch (reader.NodeType) {
                    case XmlNodeType.Element:
                        switch (reader.Name) {
                            case "text": {
                                    SpriteFont font;
                                    var s = reader.GetAttribute("font");
                                    if (!string.IsNullOrEmpty(s)) {
                                        font = fontResolver.Invoke(s);
                                    } else if(formatingStack.Count > 0){
                                        font = formatingStack.Peek().Font;
                                    } else {
                                        throw new InvalidOperationException("Need a font.");
                                    }

                                    Color color;
                                    s = reader.GetAttribute("color");
                                    if (!string.IsNullOrEmpty(s)) {
                                        color = ToColor(s);
                                    } else if(formatingStack.Count > 0) {
                                        color = formatingStack.Peek().Color;
                                    } else {
                                        throw new InvalidOperationException("Need a color.");
                                    }
                                    formatingStack.Push(new FormatInstruction(font, color));
                                }
                                break;
                            case "if": {
                                    var clause = reader.GetAttribute("clause");
                                    var condition = conditionalResolver.Invoke(clause);
                                    conditionalsStack.Push(condition);
                                }
                                break;
                            case "br": {
                                    if (lineBuffer.Count > 0) {
                                        position = WrapLine(position, lineBuffer, out currentLineHeight, compiledMarkup);
                                        currentTotalHeight += currentLineHeight;
                                    } else {
                                        position.Y += formatingStack.Peek().Font.LineSpacing;
                                        currentTotalHeight += formatingStack.Peek().Font.LineSpacing;
                                    }
                                    currentLineWidth = 0;
                                }
                                break;
                            case "img": {
                                    if (conditionalsStack.Count != 0 && !conditionalsStack.Peek()) {
                                        break;
                                    }
                                    var imgSrc = reader.GetAttribute("src");
                                    var color = Color.White;
                                    var s = reader.GetAttribute("color");
                                    if (!string.IsNullOrEmpty(s)) {
                                        color = ToColor(s);
                                    }
                                    var image = imageResolver.Invoke(imgSrc);
                                    if (position.X + image.Width > width) {
                                        position = WrapLine(position, lineBuffer, out currentLineHeight, compiledMarkup);
                                        currentTotalHeight += currentLineHeight;
                                    }
                                    lineBuffer.Add(new CompiledImageElement(image, color, position));
                                    position.X += image.Width;
                                    currentLineWidth += image.Width;
                                }
                                break;
                        }
                        break;
                    case XmlNodeType.Text: {
                            if (conditionalsStack.Count != 0 && !conditionalsStack.Peek()) {
                                break;
                            }
                            var currentFormatting = formatingStack.Peek();
                            var str = reader.Value;
                            var re = new Regex(@"\s+");
                            var words = re.Split(str);
                            var spaceX = currentFormatting.Font.MeasureString(" ").X;
                            for (var i = 0; i < words.Length; i++) {
                                var word = words[i];
                                var wordSz = currentFormatting.Font.MeasureString(word);
                                if (position.X + wordSz.X > width) {
                                    position = WrapLine(position, lineBuffer, out currentLineHeight, compiledMarkup);
                                    currentTotalHeight += currentLineHeight;
                                    maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);
                                    currentLineWidth = 0;
                                }

                                lineBuffer.Add(new CompiledTextElement(word, position, currentFormatting));
                                position.X += wordSz.X;
                                currentLineWidth += wordSz.X;
                                if (i < words.Length - 1) {
                                    position.X += spaceX;
                                    currentLineWidth += spaceX;
                                }
                            }
                        }
                        break;
                    case XmlNodeType.EndElement: {
                            switch (reader.Name) {
                                case "text":
                                    formatingStack.Pop();
                                    break;
                                case "if":
                                    conditionalsStack.Pop();
                                    break;
                            }
                        }
                        break;
                }
            }
            if (lineBuffer.Count > 0) {
                WrapLine(position, lineBuffer, out currentLineHeight, compiledMarkup);
                foreach (var element in lineBuffer) {
                    element.Position = new Vector2(element.Position.X, position.Y + currentLineHeight / 2f);
                }
                maxLineWidth = Math.Max(maxLineWidth, currentLineWidth);
                currentTotalHeight += currentLineHeight;
                compiledMarkup.Size = new Vector2(maxLineWidth, currentTotalHeight);
                compiledMarkup.AddRange(lineBuffer);
                lineBuffer.Clear();
            }
            compiledMarkup.Text = text;
        }

        private Vector2 WrapLine(Vector2 position, List<ICompiledElement> lineBuffer, out float currentLineHeight, CompiledMarkup compiledMarkup) {
            currentLineHeight = 0;
            for (var i = 0; i < lineBuffer.Count; i++) {
                currentLineHeight = Math.Max(currentLineHeight, lineBuffer[i].Size.Y);
            }
            for (var i = 0; i < lineBuffer.Count; i++) {
                lineBuffer[i].Position = new Vector2(lineBuffer[i].Position.X, position.Y + currentLineHeight / 2f);
            }
            compiledMarkup.AddRange(lineBuffer);
            lineBuffer.Clear();
            position.X = 0;
            position.Y += currentLineHeight;
            return position;
        }

        public CompiledMarkup Compile(string text, float width) {
            var compiledMarkupText = new CompiledMarkup();
            Compile(text, width, compiledMarkupText);
            return compiledMarkupText;
        }

        private static Color ToColor(string hexString) {
            if (hexString.StartsWith("#"))
                hexString = hexString.Substring(1);
            var hex = uint.Parse(hexString, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var color = Color.White;
            if (hexString.Length == 8) {
                color.A = (byte)(hex >> 24);
                color.R = (byte)(hex >> 16);
                color.G = (byte)(hex >> 8);
                color.B = (byte)(hex);
            } else if (hexString.Length == 6) {
                color.R = (byte)(hex >> 16);
                color.G = (byte)(hex >> 8);
                color.B = (byte)(hex);
            } else {
                throw new InvalidOperationException();
            }
            return color;
        }

    }
}
