using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Midnight.Controls
{
    public partial class StarField : UserControl
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly Random _rand = new();
        private readonly List<Ellipse> _stars = new();

        private const int StarCount = 120;
        private const double MinSize = 1.5;
        private const double MaxSize = 3.5;
        private const double MinSpeed = 30; // pixels per second
        private const double MaxSpeed = 90; // pixels per second

        public StarField()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromMilliseconds(30); // ~33 FPS
            _timer.Tick += OnTick;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            CreateStars();
            _timer.Start();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
        }

        private void CreateStars()
        {
            var w = ActualWidth;
            var h = ActualHeight;
            // Ensure we have a size; if not yet measured, fall back to a default.
            if (double.IsNaN(w) || w == 0) w = 800;
            if (double.IsNaN(h) || h == 0) h = 600;

            for (int i = 0; i < StarCount; i++)
            {
                var size = MinSize + _rand.NextDouble() * (MaxSize - MinSize);
                var star = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(Color.FromArgb(255,
                        (byte)_rand.Next(200, 256),
                        (byte)_rand.Next(200, 256),
                        (byte)_rand.Next(200, 256))),
                    Tag = new StarInfo
                    {
                        Speed = MinSpeed + _rand.NextDouble() * (MaxSpeed - MinSpeed),
                        X = _rand.NextDouble() * w,
                        Y = _rand.NextDouble() * h
                    }
                };

                Canvas.SetLeft(star, ((StarInfo)star.Tag).X);
                Canvas.SetTop(star, ((StarInfo)star.Tag).Y);
                StarCanvas.Children.Add(star);
                _stars.Add(star);
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (double.IsNaN(w) || w == 0) w = 800;
            if (double.IsNaN(h) || h == 0) h = 600;
            var dt = _timer.Interval.TotalSeconds;

            foreach (var star in _stars)
            {
                var info = (StarInfo)star.Tag;
                info.Y += info.Speed * dt;

                if (info.Y > h)
                {
                    info.Y = -star.Height;
                    info.X = _rand.NextDouble() * w;
                    info.Speed = MinSpeed + _rand.NextDouble() * (MaxSpeed - MinSpeed);
                }

                Canvas.SetLeft(star, info.X);
                Canvas.SetTop(star, info.Y);
            }
        }

        private class StarInfo
        {
            public double X;
            public double Y;
            public double Speed;
        }
    }
}
