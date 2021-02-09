﻿//MIT, 2020-present, WinterDev
using System;

using Typography.OpenFont;
using Typography.OpenFont.Extensions;

using Typography.FontCollections;
using Typography.TextLayout;
using Typography.TextBreak;

using PixelFarm.Drawing;

namespace Typography.Text
{
    public readonly struct FormattedGlyphPlanListHolder : IFormattedGlyphPlanList
    {
        public readonly FormattedGlyphPlanSeq _chainFmtGlyphPlans;
        public FormattedGlyphPlanListHolder(FormattedGlyphPlanSeq fmtGlyphPlans)
        {
            _chainFmtGlyphPlans = fmtGlyphPlans;
        }
    }

    public class TextServiceClient
    {
        readonly OpenFontTextService _openFontTextService; //owner
        readonly VirtualTextSpanPrinter _p;

        internal TextServiceClient(OpenFontTextService openFontTextService)
        {
            _openFontTextService = openFontTextService;
            _p = new VirtualTextSpanPrinter();
            _p.BuiltInAlternativeTypefaceSelector = (int codepoint, AltTypefaceSelectorBase userSelector, out Typeface typeface) => _openFontTextService.TryGetAlternativeTypefaceFromCodepoint(codepoint, userSelector, out typeface);
        }
        public OpenFontTextService TextService => _openFontTextService;

        public ScriptLang CurrentScriptLang
        {
            get => _p.ScriptLang;
            set => _p.ScriptLang = value;
        }
        public void EnableGsubGpos(bool value)
        {
            _p.EnableGsubGpos(value);
        }


        readonly FormattedGlyphPlanSeqPool _fmtGlyphPlanList = new FormattedGlyphPlanSeqPool();
        readonly System.Collections.Generic.List<bool> _isSurrogates = new System.Collections.Generic.List<bool>();

        public void CalculateUserCharGlyphAdvancePos(in Typography.Text.TextBufferSpan textBufferSpan, RequestFont font, ref TextSpanMeasureResult measureResult)
        {
            ResolvedFont resFont1 = _openFontTextService.ResolveFont(font);
            Typeface typeface = resFont1.Typeface;
            _p.Typeface = typeface;
            _p.FontSizeInPoints = font.SizeInPoints;

            _fmtGlyphPlanList.Clear();
            _isSurrogates.Clear();

            if (textBufferSpan.IsUtf32Buffer)
            {
                int[] rawBuffer = textBufferSpan.GetRawUtf32Buffer();
                _p.PrepareFormattedStringList(rawBuffer, textBufferSpan.start, textBufferSpan.len, _fmtGlyphPlanList);

                int pos1 = textBufferSpan.start;
                for (int i = 0; i < textBufferSpan.len; ++i)
                {
                    int c = rawBuffer[pos1];
                    InputReader.GetChars(c, out char c0, out char c1);
                    if (c1 != 0)
                    {
                        _isSurrogates.Add(true);
                    }
                    else
                    {
                        _isSurrogates.Add(false);
                    }
                    pos1++;
                }
            }
            else
            {
                char[] rawBuffer = textBufferSpan.GetRawUtf16Buffer();
                _p.PrepareFormattedStringList(rawBuffer, textBufferSpan.start, textBufferSpan.len, _fmtGlyphPlanList);

                int pos1 = textBufferSpan.start;
                for (int i = 0; i < textBufferSpan.len; ++i)
                {
                    char c = rawBuffer[pos1];
                    if (char.IsHighSurrogate(c) && i < textBufferSpan.len - 1)
                    {
                        char c2 = rawBuffer[pos1 + 1];
                        if (char.IsLowSurrogate(c2))
                        {
                            _isSurrogates.Add(true);
                            _isSurrogates.Add(true);
                            ++i;
                            ++pos1;
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        throw new NotSupportedException();
                    }
                    else
                    {
                        _isSurrogates.Add(false);
                    }
                    ++pos1;
                }
            }


            //then measure the result 
            short minOffsetY = 0;
            short maxOffsetY = 0;
            int outputTotalW = 0;
            bool hasSomeExtraOffsetY = false;

            int pos = 0;
            if (_fmtGlyphPlanList.Count > 0)
            {
                FormattedGlyphPlanSeq fmtSeq = _fmtGlyphPlanList.GetFirst();
                while (fmtSeq != null)
                {
                    GlyphPlanSequence seq = fmtSeq.Seq;
                    int seqLen = seq.Count;
                    //
                    ResolvedFont resFont = fmtSeq.ResolvedFont;
                    float scale1 = resFont.GetScaleToPixelFromPointUnit();

                    //1. prefix whitespace count
                    int ws_count = fmtSeq.PrefixWhitespaceCount;
                    for (int n = 0; n < ws_count; ++n)
                    {
                        outputTotalW += measureResult.outputXAdvances[pos] = resFont.WhitespaceWidth;
                        pos++;
                    }

                    int begin_glyph_at = pos;
                    int max_cp_offset = -1;
                    for (int s = 0; s < seqLen; ++s)
                    {
                        //for each glyph index
                        UnscaledGlyphPlan glyphPlan = seq[s];
                        if (glyphPlan.OffsetY != 0)
                        {
                            hasSomeExtraOffsetY = true;
                            if (minOffsetY > glyphPlan.OffsetY)
                            {
                                minOffsetY = glyphPlan.OffsetY;
                            }
                            if (maxOffsetY < glyphPlan.OffsetY)
                            {
                                maxOffsetY = glyphPlan.OffsetY;
                            }
                        }
                        outputTotalW += measureResult.outputXAdvances[begin_glyph_at + glyphPlan.input_cp_offset] = (int)Math.Round(glyphPlan.AdvanceX * scale1);
                        max_cp_offset = Math.Max(max_cp_offset, glyphPlan.input_cp_offset);
                        //if (_isSurrogates[pos])
                        //{
                        //    outputTotalW += measureResult.outputXAdvances[pos] = (int)Math.Round(glyphPlan.AdvanceX * scale1);
                        //    pos += 2;
                        //}
                        //else
                        //{
                        //    outputTotalW += measureResult.outputXAdvances[pos] = (int)Math.Round(glyphPlan.AdvanceX * scale1);
                        //    pos++;
                        //}
                    }


                    ws_count = fmtSeq.PostfixWhitespaceCount;
                    if (max_cp_offset > 0)
                    {
                        pos += max_cp_offset;
                        pos++;
                    }

                    for (int n = 0; n < ws_count; ++n)
                    {
                        outputTotalW += measureResult.outputXAdvances[pos] = resFont.WhitespaceWidth;
                        pos++;
                    }

                    //
                    fmtSeq = fmtSeq.Next;
                }

            }


            //------
            float scale = resFont1.GetScaleToPixelFromPointUnit();
            measureResult.outputTotalW = outputTotalW;
            measureResult.lineHeight = (ushort)Math.Round(typeface.CalculateMaxLineClipHeight() * scale);

            if (hasSomeExtraOffsetY)
            {
                measureResult.minOffsetY = (short)Math.Round(minOffsetY * scale);
                measureResult.maxOffsetY = (short)Math.Round(maxOffsetY * scale);
                if (measureResult.maxOffsetY != 0 || measureResult.minOffsetY != 0)
                {
                    measureResult.hasSomeExtraOffsetY = true;
                }
            }

        }
        public void PrepareFormattedStringList(int[] textBuffer, int startAt, int len, FormattedGlyphPlanSeqProvider fmtGlyphs)
        {
            _p.PrepareFormattedStringList(textBuffer, startAt, len, fmtGlyphs);
            fmtGlyphs.IsRightToLeftDirection = _p.NeedRightToLeftArr;
        }
        public void PrepareFormattedStringList(char[] textBuffer, int startAt, int len, FormattedGlyphPlanSeqProvider fmtGlyphs)
        {
            _p.PrepareFormattedStringList(textBuffer, startAt, len, fmtGlyphs);
            fmtGlyphs.IsRightToLeftDirection = _p.NeedRightToLeftArr;
        }

        public float CalculateScaleToPixelsFromPoint(RequestFont font) => (_openFontTextService.ResolveFont(font) is ResolvedFont resolvedFont) ? resolvedFont.GetScaleToPixelFromPointUnit() : 0;

        public float MeasureWhitespace(RequestFont f)
        {
            ResolvedFont resolvedFont = _openFontTextService.ResolveFont(f);
            if (resolvedFont != null)
            {
                return resolvedFont.WhitespaceWidthF;
            }
            return 0;
        }
        public void SetCurrentFont(Typeface typeface, float sizeInPts, ScriptLang sclang)
        {
            _p.ScriptLang = sclang;
            _p.Typeface = typeface;
            _p.FontSizeInPoints = sizeInPts;

            _p.UpdateGlyphLayoutSettings();
        }
        public void SetCurrentFont(Typeface typeface, float sizeInPts, ScriptLang sclang, PositionTechnique posTech)
        {
            _p.ScriptLang = sclang;
            _p.PositionTechnique = posTech;
            _p.Typeface = typeface;
            _p.FontSizeInPoints = sizeInPts;

            _p.UpdateGlyphLayoutSettings();
        }

        public GlyphPlanSequence CreateGlyphPlanSeq(in Typography.Text.TextBufferSpan textBufferSpan, Typeface typeface, float sizeInPts)
        {
            _p.Typeface = typeface;
            _p.FontSizeInPoints = sizeInPts;

            return _p.CreateGlyphPlanSeq(textBufferSpan);
        }

        public GlyphPlanSequence CreateGlyphPlanSeq(in Typography.Text.TextBufferSpan textBufferSpan, RequestFont font)
        {
            return CreateGlyphPlanSeq(textBufferSpan, _openFontTextService.ResolveFont(font).Typeface, font.SizeInPoints);
        }
        public GlyphPlanSequence CreateGlyphPlanSeq(in TextBufferSpan textBufferSpan, ResolvedFont font)
        {
            return CreateGlyphPlanSeq(textBufferSpan, font.Typeface, font.SizeInPoints);
        }

        readonly MeasureStringArgs _measureResult = new MeasureStringArgs();

        public Size MeasureString(in Typography.Text.TextBufferSpan bufferSpan, RequestFont font)
        {
            //TODO: review here
            Typeface typeface = _openFontTextService.ResolveFont(font).Typeface;
            _p.Typeface = typeface;
            _p.FontSizeInPoints = font.SizeInPoints;

            _measureResult.Reset();
            _p.MeasureString(bufferSpan, _measureResult);

            return new Size(_measureResult.Width, _measureResult.Height);
        }

        public Size MeasureString(in Typography.Text.TextBufferSpan textBufferSpan, ResolvedFont font)
        {
            //TODO: review here
            Typeface typeface = font.Typeface;
            _p.Typeface = typeface;
            _p.FontSizeInPoints = font.SizeInPoints;
            var bufferSpan = new Typography.Text.TextBufferSpan(textBufferSpan.GetRawUtf16Buffer(), textBufferSpan.start, textBufferSpan.len);

            _measureResult.Reset();
            _p.MeasureString(bufferSpan, _measureResult);

            return new Size(_measureResult.Width, _measureResult.Height);
        }
        public void MeasureString(in Typography.Text.TextBufferSpan bufferSpan, RequestFont font, int limitWidth, out int charFit, out int charFitWidth)
        {
            //TODO: review here ***
            Typeface typeface = _openFontTextService.ResolveFont(font).Typeface;
            _p.Typeface = typeface;
            _p.FontSizeInPoints = font.SizeInPoints;

            _measureResult.Reset();
            _measureResult.LimitWidth = limitWidth;
            _p.MeasureString(bufferSpan, _measureResult);

            charFit = _measureResult.CharFit;
            charFitWidth = _measureResult.CharFitWidth;
        }

        public float MeasureBlankLineHeight(RequestFont font)
        {
            ResolvedFont resolvedFont = ResolveFont(font);
            return resolvedFont.LineSpacingInPixels;
        }


        public void BreakToLineSegments(in TextBufferSpan textBufferSpan, WordVisitor wordVisitor) => _p.BreakToLineSegments(textBufferSpan, wordVisitor);

        public ResolvedFont ResolveFont(RequestFont reqFont) => _openFontTextService.ResolveFont(reqFont);

        public bool TryGetAlternativeTypefaceFromCodepoint(int codepoint, AltTypefaceSelectorBase selector, out Typeface found) => _openFontTextService.TryGetAlternativeTypefaceFromCodepoint(codepoint, selector, out found);

        public AlternativeTypefaceSelector AlternativeTypefaceSelector
        {
            get => (AlternativeTypefaceSelector)_p.AlternativeTypefaceSelector;
            set => _p.AlternativeTypefaceSelector = value;
        }

        public bool Eq(ReqFontSpec spec1, ReqFontSpec spec2)
        {
            return spec1.GetReqKey() == spec2.GetReqKey();
        }
        public bool Eq(ResolvedFont resolved1, ReqFontSpec spec2)
        {
            ResolvedFont resolved2 = ReqFontSpec.GetResolvedFont1<ResolvedFont>(spec2);
            if (resolved1 == resolved2)
            {
                return true;
            }

            if (resolved2 == null)
            {
                //no cache resolved data,
                if (spec2.Name == resolved1.Typeface.Name)
                {

                }
                return false;
            }
            else
            {
                return resolved1.RuntimeResolvedKey == resolved2.RuntimeResolvedKey;
            }

        }
        public bool Eq(ResolvedFont resolvedFont1, ResolvedFont resolvedFont2)
        {
            return resolvedFont1.RuntimeResolvedKey == resolvedFont2.RuntimeResolvedKey;
        }
    }
}