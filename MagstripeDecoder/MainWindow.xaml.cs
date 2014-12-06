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

using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.IO;
using System.Windows;

namespace MagstripeDecoder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Decoder DecoderTrack1;
        private Decoder DecoderTrack2;
        private WasapiCapture Capture;

        public MainWindow()
        {
            DecoderTrack1 = new Decoder(44100);
            DecoderTrack1.BitMode = BitMode.Bits7;
            DecoderTrack1.ReceiveChar += ReceiveCharacter;
            DecoderTrack2 = new Decoder(44100);
            DecoderTrack2.BitMode = BitMode.Bits5;
            DecoderTrack2.ReceiveChar += ReceiveCharacter;
            InitializeComponent();
            PopulateDevices();
        }

        private void ReceiveCharacter(object sender, ReceiveCharEventArgs e)
        {
            if (sender == DecoderTrack1)
            {
                Track1Box.Text += Char.ConvertFromUtf32(e.Chr);
            }
            else
            {
                Track2Box.Text += Char.ConvertFromUtf32(e.Chr);
            }
        }

        private void ReceiveWave(object sender, WaveInEventArgs e)
        {
            var samples = ParseStereo(e.Buffer, e.BytesRecorded);
            Dispatcher.Invoke(new Action<double[]>(DecoderTrack1.AddSamples), samples.Item2);
            Dispatcher.Invoke(new Action<double[]>(DecoderTrack2.AddSamples), samples.Item1);
        }

        private void PopulateDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            DeviceBox.ItemsSource = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            DeviceBox.SelectedIndex = 0;
        }

        private void StartRecording()
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            LoadButton.IsEnabled = false;
            Capture = new WasapiCapture((MMDevice)DeviceBox.SelectedItem);
            Capture.WaveFormat = new WaveFormat(44100, 16, 2);
            Capture.DataAvailable += ReceiveWave;
            Capture.RecordingStopped += RecordingStopped;
            Capture.StartRecording();
        }

        private void StopRecording()
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            LoadButton.IsEnabled = true;
            if (Capture != null)
            {
                Capture.DataAvailable -= ReceiveWave;
                Capture.RecordingStopped -= RecordingStopped;
                Capture.StopRecording();
                Capture = null;
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.DefaultExt = ".wav";
            dialog.Filter = "Sound files|*.wav";
            var result = dialog.ShowDialog();
            if (result == true)
            {
                var samples = LoadWaveFile(dialog.FileName);
                DecoderTrack1.AddSamples(samples);
                DecoderTrack2.AddSamples(samples);
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartRecording();
        }

        private void RecordingStopped(object sender, StoppedEventArgs e)
        {
            StopRecording();
            MessageBox.Show(e.Exception.ToString());
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        private double[] LoadWaveFile(string filename)
        {
            byte[] data = File.ReadAllBytes(filename);
            double[] samples = new double[(data.Length - 44) / 2];
            for (int i = 0; i < samples.Length; ++i)
            {
                short num = (short)(data[44 + i * 2] | (data[45 + i * 2] << 8));
                samples[i] = num / 32768.0;
            }
            return samples;
        }

        private Tuple<double[], double[]> ParseStereo(byte[] data, int len)
        {
            int numSamples = len / 4;
            double[] leftSamples = new double[numSamples];
            double[] rightSamples = new double[numSamples];
            for (int i = 0; i < leftSamples.Length; ++i)
            {
                short num = (short)(data[i * 4] | (data[1 + i * 4] << 8));
                leftSamples[i] = num / 32768.0;
                num = (short)(data[2 + i * 4] | (data[3 + i * 4] << 8));
                rightSamples[i] = num / 32768.0;
            }
            return new Tuple<double[], double[]>(leftSamples, rightSamples);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            DecoderTrack1.Clear();
            DecoderTrack2.Clear();
            Track1Box.Text = "";
            Track2Box.Text = "";
        }
    }
}
