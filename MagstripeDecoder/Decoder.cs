// Copyright 2014 Jacobo Tarrio Barreiro. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;

namespace MagstripeDecoder
{
    public enum DecoderState
    {
        NoCard, Sync, Decoding
    }

    public enum BitMode
    {
        Bits5, Bits7
    }

    public class ReceiveCharEventArgs : EventArgs
    {
        public char Chr { get; private set; }
        public ReceiveCharEventArgs(char chr)
        {
            Chr = chr;
        }
    }

    class Decoder
    {
        private PeakDetector peakDetector;
        private BitDecoder bitDecoder;
        private SymbolDecoder symbolDecoder;

        public string Text { get { return symbolDecoder.Text; } }

        public BitMode BitMode
        {
            get { return symbolDecoder.BitMode; }
            set { symbolDecoder.BitMode = value; }
        }

        public Decoder(int sampleRate)
        {
            peakDetector = new PeakDetector(this);
            bitDecoder = new BitDecoder(this, sampleRate);
            symbolDecoder = new SymbolDecoder(this);
            Clear();
        }

        public void Clear()
        {
            peakDetector.Clear();
            bitDecoder.Clear();
            symbolDecoder.Clear();
        }

        public void AddSamples(double[] samples)
        {
            peakDetector.AddSamples(samples);
        }

        private DecoderState State { get { return bitDecoder.State; } }

        public delegate void ReceiveCharHandler(object sender, ReceiveCharEventArgs e);
        public event ReceiveCharHandler ReceiveChar;

        private void OnReceiveChar(char chr)
        {
            ReceiveChar(this, new ReceiveCharEventArgs(chr));
        }

        private class PeakDetector
        {
            private Decoder decoder;
            private int counter;
            private double noiseFloor;
            private int edgeDirection;
            private double highPeak;
            private double lowPeak;
            private double prevPeak;

            public PeakDetector(Decoder decoder)
            {
                this.decoder = decoder;
            }

            public void Clear()
            {
                counter = 0;
                noiseFloor = .05;
                edgeDirection = 0;
                highPeak = -1;
                lowPeak = 1;
                prevPeak = 0;
            }

            public void AddSamples(double[] samples)
            {
                for (int i = 0; i < samples.Length; ++i)
                {
                    var sample = samples[i];
                    if (decoder.State == DecoderState.NoCard && sample > -2 * noiseFloor && sample < 2 * noiseFloor)
                    {
                        noiseFloor = (noiseFloor * 9999 + Math.Abs(sample)) / 10000;
                    }
                    else if (sample > 0)
                    {
                        highPeak = Math.Max(highPeak, sample);
                        lowPeak = 1;
                        if (sample < 0.9 * highPeak && highPeak > 0.25 * prevPeak && edgeDirection <= 0)
                        {
                            prevPeak = highPeak;
                            decoder.bitDecoder.ReceivePeak(i + counter);
                            edgeDirection = 1;
                        }
                    }
                    else
                    {
                        lowPeak = Math.Min(lowPeak, sample);
                        highPeak = -1;
                        if (sample > 0.9 * lowPeak && lowPeak < -0.25 * prevPeak && edgeDirection >= 0)
                        {
                            prevPeak = -lowPeak;
                            decoder.bitDecoder.ReceivePeak(i + counter);
                            edgeDirection = -1;
                        }
                    }
                }
                counter += samples.Length;
            }
        }

        private class BitDecoder
        {
            private Decoder decoder;
            private int prevPeakSample;
            private double clockSamples;
            private int countedPeaks;
            private bool currentBitIsOne;
            private int stopSamples;

            public DecoderState State { get; set; }

            public BitDecoder(Decoder decoder, int sampleRate)
            {
                this.decoder = decoder;
                this.stopSamples = (int)(0.150 * sampleRate);
                Clear();
            }

            public void Clear()
            {
                State = DecoderState.NoCard;
                prevPeakSample = -1;
                clockSamples = -1;
                countedPeaks = 0;
                currentBitIsOne = false;
            }

            public void ReceivePeak(int sampleNum)
            {
                if (prevPeakSample < 0)
                {
                    prevPeakSample = sampleNum;
                    return;
                }
                int samplesFromPrev = sampleNum - prevPeakSample;
                if (samplesFromPrev > stopSamples || (State != DecoderState.NoCard && samplesFromPrev > 1.5 * clockSamples))
                {
                    decoder.symbolDecoder.Stop();
                    State = DecoderState.NoCard;
                    clockSamples = -1;
                    countedPeaks = 0;
                    prevPeakSample = sampleNum;
                    return;
                }
                switch (State)
                {
                    case DecoderState.NoCard:
                        if (clockSamples < 0)
                        {
                            clockSamples = samplesFromPrev;
                            return;
                        }
                        clockSamples = (clockSamples * 3 + samplesFromPrev) / 4;
                        prevPeakSample = sampleNum;
                        ++countedPeaks;
                        if (countedPeaks == 8)
                        {
                            State = DecoderState.Sync;
                            currentBitIsOne = false;
                        }
                        return;
                    case DecoderState.Sync:
                        if (samplesFromPrev < 0.75 * clockSamples)
                        {
                            currentBitIsOne = true;
                            State = DecoderState.Decoding;
                        }
                        else
                        {
                            currentBitIsOne = false;
                            clockSamples = (clockSamples * 3 + samplesFromPrev) / 4;
                            prevPeakSample = sampleNum;
                        }
                        return;
                    case DecoderState.Decoding:
                        if (samplesFromPrev < 0.75 * clockSamples)
                        {
                            currentBitIsOne = true;
                            State = DecoderState.Decoding;
                        }
                        else
                        {
                            decoder.symbolDecoder.ReceiveBit(currentBitIsOne ? 1 : 0);
                            currentBitIsOne = false;
                            clockSamples = (clockSamples * 3 + samplesFromPrev) / 4;
                            prevPeakSample = sampleNum;
                        }
                        return;
                }
            }
        }

        private class SymbolDecoder
        {
            private Decoder decoder;
            private bool decoding = false;
            private int bitsReceived = 0;
            private int currentSymbol = 0;
            private BitMode bitMode;
            private int bits;
            private int sentinel;
            private int symbolMask;
            private int byteMask;
            private int asciiOffset;

            public SymbolDecoder(Decoder decoder)
            {
                this.decoder = decoder;
            }

            public string Text { get; private set; }

            public BitMode BitMode { get { return bitMode; } set { SetBitMode(value); } }

            public void ReceiveBit(int bit)
            {
                currentSymbol = ((bit << (bits - 1)) | (currentSymbol >> 1)) & symbolMask;
                if (!decoding && currentSymbol == sentinel)
                {
                    bitsReceived = 0;
                    decoding = true;
                }
                if (decoding)
                {
                    if (bitsReceived == 0)
                    {
                        char chr = (char)((currentSymbol & byteMask) + asciiOffset);
                        Text += Char.ConvertFromUtf32(chr);
                        decoder.OnReceiveChar(chr);
                    }
                    bitsReceived = (bitsReceived + 1) % bits;
                }
            }

            public void Stop()
            {
                decoding = false;
                bitsReceived = 0;
            }

            public void Clear()
            {
                Stop();
                Text = "";
            }

            private void SetBitMode(BitMode mode)
            {
                if (mode == BitMode.Bits5)
                {
                    sentinel = 0x0b;
                    asciiOffset = 0x30;
                    bits = 5;
                }
                else
                {
                    sentinel = 0x45;
                    asciiOffset = 0x20;
                    bits = 7;
                }
                symbolMask = (1 << bits) - 1;
                byteMask = (1 << (bits - 1)) - 1;
                bitMode = mode;
            }
        }
    }
}
