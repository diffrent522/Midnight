using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace Midnight.Services
{
    public static class AnimationService
    {
        private static readonly Point CenterOrigin = new Point(0.5, 0.5);
        private static readonly CubicEase EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static bool _initialized;

        public static bool IsEnabled { get; set; } = true;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                ButtonBase.ClickEvent,
                new RoutedEventHandler((s, e) => SoundService.PlayClick()));

            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.MenuItem),
                System.Windows.Controls.MenuItem.ClickEvent,
                new RoutedEventHandler((s, e) => SoundService.PlayClick()));

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnButtonDown));

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                UIElement.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnButtonUp));

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                UIElement.MouseEnterEvent,
                new MouseEventHandler(OnButtonEnter));

            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                UIElement.MouseLeaveEvent,
                new MouseEventHandler(OnButtonLeave));

            EventManager.RegisterClassHandler(
                typeof(NavigationViewItem),
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnNavDown));

            EventManager.RegisterClassHandler(
                typeof(NavigationViewItem),
                UIElement.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnNavUp));

            EventManager.RegisterClassHandler(
                typeof(ListBoxItem),
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnListBoxItemDown));

            EventManager.RegisterClassHandler(
                typeof(ListBoxItem),
                UIElement.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnListBoxItemUp));
        }

        private static void OnButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.IsEnabled)
            {
                if (fe is CheckBox || fe is RadioButton) return;
                AnimateScale(fe, 0.965, 75);
            }
        }

        private static void OnButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.IsEnabled)
            {
                if (fe is CheckBox || fe is RadioButton) return;
                AnimateScale(fe, fe.IsMouseOver ? 1.02 : 1.0, 150);
            }
        }

        private static void OnButtonEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.IsEnabled)
            {
                if (fe is CheckBox || fe is RadioButton) return;
                AnimateScale(fe, 1.02, 130);
            }
        }

        private static void OnButtonLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                if (fe is CheckBox || fe is RadioButton) return;
                AnimateScale(fe, 1.0, 150);
            }
        }

        private static void OnNavDown(object sender, MouseButtonEventArgs e)
        {
            SoundService.PlayClick();
            if (sender is FrameworkElement fe)
            {
                AnimateScale(fe, 0.975, 75);
            }
        }

        private static void OnNavUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                AnimateScale(fe, 1.0, 150);
            }
        }

        private static void OnListBoxItemDown(object sender, MouseButtonEventArgs e)
        {
            if (HasParentButton(e.OriginalSource as DependencyObject)) return;
            SoundService.PlayClick();
            if (sender is FrameworkElement fe)
            {
                AnimateScale(fe, 0.99, 75);
            }
        }

        private static void OnListBoxItemUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                AnimateScale(fe, 1.0, 150);
            }
        }

        private static bool HasParentButton(DependencyObject? obj)
        {
            while (obj != null)
            {
                if (obj is ButtonBase) return true;
                if (obj is ListBoxItem) return false;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        private static void AnimateScale(FrameworkElement element, double targetScale, int durationMs)
        {
            if (!IsEnabled) return;
            try
            {
                var scale = GetOrCreateScaleTransform(element);
                var anim = new DoubleAnimation
                {
                    To = targetScale,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = EaseOut
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            catch
            {
            }
        }

        private static ScaleTransform GetOrCreateScaleTransform(FrameworkElement element)
        {
            if (element.RenderTransform is ScaleTransform st && !st.IsFrozen)
            {
                return st;
            }

            if (element.RenderTransform is TransformGroup tg)
            {
                foreach (var child in tg.Children)
                {
                    if (child is ScaleTransform childSt && !childSt.IsFrozen)
                    {
                        return childSt;
                    }
                }
                var newSt = new ScaleTransform(1.0, 1.0);
                tg.Children.Add(newSt);
                return newSt;
            }

            var freshSt = new ScaleTransform(1.0, 1.0);
            element.RenderTransformOrigin = CenterOrigin;
            element.RenderTransform = freshSt;
            return freshSt;
        }
    }
}

