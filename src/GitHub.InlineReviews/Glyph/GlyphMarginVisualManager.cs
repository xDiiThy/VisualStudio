﻿using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Collections.Generic;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Classification;

namespace GitHub.InlineReviews.Glyph.Implementation
{
    internal class GlyphMarginVisualManager<TGlyphTag> where TGlyphTag: ITag
    {
        readonly IEditorFormatMap editorFormatMap;
        readonly IGlyphFactory<TGlyphTag> glyphFactory;
        readonly Grid glyphMarginGrid;
        readonly IWpfTextViewMargin margin;
        readonly string marginPropertiesName;
        readonly IWpfTextView textView;
        readonly Dictionary<Type, Canvas> visuals;

        Dictionary<UIElement, GlyphData<TGlyphTag>> glyphs;

        public GlyphMarginVisualManager(IWpfTextView textView, IGlyphFactory<TGlyphTag> glyphFactory, Func<Grid> gridFactory,
            IWpfTextViewMargin margin, IEditorFormatMap editorFormatMap, string marginPropertiesName, double marginWidth)
        {
            this.textView = textView;
            this.margin = margin;
            this.marginPropertiesName = marginPropertiesName;
            this.editorFormatMap = editorFormatMap;
            this.editorFormatMap.FormatMappingChanged += OnFormatMappingChanged;
            this.textView.Closed += new EventHandler(OnTextViewClosed);
            this.glyphFactory = glyphFactory;

            glyphs = new Dictionary<UIElement, GlyphData<TGlyphTag>>();
            visuals = new Dictionary<Type, Canvas>();
            glyphMarginGrid = gridFactory.Invoke();
            glyphMarginGrid.Width = marginWidth;
            UpdateBackgroundColor();

            foreach (Type type in glyphFactory.GetTagTypes())
            {
                if (!visuals.ContainsKey(type))
                {
                    var element = new Canvas();
                    element.ClipToBounds = true;
                    glyphMarginGrid.Children.Add(element);
                    visuals[type] = element;
                }
            }
        }

        public FrameworkElement MarginVisual => glyphMarginGrid;

        public void AddGlyph(TGlyphTag tag, SnapshotSpan span)
        {
            var textViewLines = textView.TextViewLines;
            var glyphType = tag.GetType();
            if (textView.TextViewLines.IntersectsBufferSpan(span))
            {
                var startingLine = GetStartingLine(textViewLines, span) as IWpfTextViewLine;
                if (startingLine != null)
                {
                    var element = (FrameworkElement)glyphFactory.GenerateGlyph(startingLine, tag);
                    if (element != null)
                    {
                        // center on margin
                        //element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        //double length = (17.0 - element.DesiredSize.Width) / 2.0;
                        //Canvas.SetLeft(element, length);

                        // size to margin
                        element.Width = glyphMarginGrid.Width;
                        element.Height = startingLine.Height;

                        var data = new GlyphData<TGlyphTag>(span, tag, element);
                        data.SetTop(startingLine.TextTop - textView.ViewportTop);
                        glyphs[element] = data;
                        visuals[glyphType].Children.Add(element);
                    }
                }
            }
        }

        static void SetMinimumWithAndHeight(FrameworkElement element, double width, double height)
        {
            if (element.Width < width)
            {
                element.Width = width;
            }

            if (element.Height < height)
            {
                element.Height = height;
            }
        }

        public void RemoveGlyphsByVisualSpan(SnapshotSpan span)
        {
            var list = new List<UIElement>();
            foreach (var pair in glyphs)
            {
                var data = pair.Value;
                if (data.VisualSpan.HasValue && span.IntersectsWith(data.VisualSpan.Value))
                {
                    list.Add(pair.Key);
                    visuals[data.GlyphType].Children.Remove(data.Glyph);
                }
            }

            foreach (var element in list)
            {
                glyphs.Remove(element);
            }
        }

        public void SetSnapshotAndUpdate(ITextSnapshot snapshot, IList<ITextViewLine> newOrReformattedLines, IList<ITextViewLine> translatedLines)
        {
            if (glyphs.Count > 0)
            {
                var dictionary = new Dictionary<UIElement, GlyphData<TGlyphTag>>(glyphs.Count);
                foreach (var pair in glyphs)
                {
                    var data = pair.Value;
                    if (!data.VisualSpan.HasValue)
                    {
                        dictionary[pair.Key] = data;
                        continue;
                    }

                    data.SetSnapshot(snapshot);
                    SnapshotSpan bufferSpan = data.VisualSpan.Value;
                    if (!textView.TextViewLines.IntersectsBufferSpan(bufferSpan) || GetStartingLine(newOrReformattedLines, bufferSpan) != null)
                    {
                        visuals[data.GlyphType].Children.Remove(data.Glyph);
                        continue;
                    }

                    dictionary[data.Glyph] = data;
                    var startingLine = GetStartingLine(translatedLines, bufferSpan);
                    if (startingLine != null)
                    {
                        data.SetTop(startingLine.TextTop - textView.ViewportTop);
                    }
                }

                glyphs = dictionary;
            }
        }
        static ITextViewLine GetStartingLine(IList<ITextViewLine> lines, Span span)
        {
            if (lines.Count > 0)
            {
                int num = 0;
                int count = lines.Count;
                while (num < count)
                {
                    int middle = (num + count) / 2;
                    var middleLine = lines[middle];
                    if (span.Start < middleLine.Start)
                    {
                        count = middle;
                    }
                    else
                    {
                        if (span.Start >= middleLine.EndIncludingLineBreak)
                        {
                            num = middle + 1;
                            continue;
                        }

                        return middleLine;
                    }
                }

                var line = lines[lines.Count - 1];
                if (line.EndIncludingLineBreak == line.Snapshot.Length && span.Start == line.EndIncludingLineBreak)
                {
                    return line;
                }
            }

            return null;
        }

        void OnFormatMappingChanged(object sender, FormatItemsEventArgs e)
        {
            if (e.ChangedItems.Contains(marginPropertiesName))
            {
                UpdateBackgroundColor();
            }
        }

        void OnTextViewClosed(object sender, EventArgs e)
        {
            editorFormatMap.FormatMappingChanged -= OnFormatMappingChanged;
        }

        void UpdateBackgroundColor()
        {
            // set background color for children
            var properties = editorFormatMap.GetProperties(marginPropertiesName);
            if (properties.Contains("BackgroundColor"))
            {
                var backgroundColor = (Color)properties["BackgroundColor"];
                ImageThemingUtilities.SetImageBackgroundColor(glyphMarginGrid, backgroundColor);
            }

            // set background color for margin
            //var properties = editorFormatMap.GetProperties(marginPropertiesName);
            //if (properties.Contains("BackgroundColor"))
            //{
            //    glyphMarginGrid.Background = new SolidColorBrush((Color)properties["BackgroundColor"]);
            //    glyphMarginGrid.Background.Freeze();
            //}
            //else if (properties.Contains("Background"))
            //{
            //    glyphMarginGrid.Background = (Brush)properties["Background"];
            //}
            //else
            //{
            //    glyphMarginGrid.Background = Brushes.Transparent;
            //}

            //var background = glyphMarginGrid.Background as SolidColorBrush;
            //if (background != null)
            //{
            //    ImageThemingUtilities.SetImageBackgroundColor(glyphMarginGrid, background.Color);
            //}
        }
    }
}

