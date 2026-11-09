using System;
using System.Drawing;
using System.Threading;

namespace MacroDeck
{
    // One instance per cycle: all variation is relative to the saved recording,
    // never to the output of the previous cycle. Saved events remain untouched.
    internal sealed class PlaybackVariation
    {
        private readonly bool enabled;
        private readonly Rectangle[] screens;
        private readonly int offsetX, offsetY;
        private readonly double timeScale;
        private long previousTime;

        public PlaybackVariation(bool enabled, Random random, Rectangle[] screens)
        {
            this.enabled = enabled; this.screens = screens;
            offsetX = enabled ? random.Next(-1, 2) : 0;
            offsetY = enabled ? random.Next(-1, 2) : 0;
            timeScale = enabled ? 0.97 + random.NextDouble() * 0.06 : 1;
        }

        public Point Position(Point original)
        {
            if (!enabled) return original;
            return PlaybackMotion.NearestScreenPoint((long)original.X + offsetX, (long)original.Y + offsetY, screens);
        }

        public MacroEvent Event(MacroEvent original)
        {
            if (!enabled || !IsMouse(original.Kind)) return original;
            Point point = Position(new Point(original.X, original.Y));
            return new MacroEvent {
                Kind = original.Kind, Code = original.Code, X = point.X, Y = point.Y,
                Wheel = original.Wheel, TimeMs = original.TimeMs, TimeUs = original.TimeUs
            };
        }

        public long Time(long originalMicroseconds)
        {
            if (!enabled) return originalMicroseconds;
            double scaled = originalMicroseconds * timeScale;
            long nominal = scaled >= Int64.MaxValue ? Int64.MaxValue : (long)Math.Round(scaled);
            // One scale per cycle changes speed and hold duration together, without
            // random delays accumulating or key-up overtaking its key-down.
            previousTime = Math.Max(previousTime, nominal);
            return previousTime;
        }

        private static bool IsMouse(string kind)
        {
            return kind == "move" || kind == "mouseDown" || kind == "mouseUp" || kind == "wheel";
        }
    }

    internal static class PlaybackMotion
    {
        public static Point NearestScreenPoint(long x, long y, Rectangle[] screens)
        {
            if (screens == null || screens.Length == 0) throw new InvalidOperationException("Nenhum monitor disponível para o movimento do cursor.");
            Point nearest = Point.Empty;
            double bestDistance = Double.MaxValue;
            foreach (Rectangle screen in screens)
            {
                if (screen.Width <= 0 || screen.Height <= 0) continue;
                int px = (int)Math.Max(screen.Left, Math.Min((long)screen.Right - 1, x));
                int py = (int)Math.Max(screen.Top, Math.Min((long)screen.Bottom - 1, y));
                double dx = x - (double)px, dy = y - (double)py;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance) { bestDistance = distance; nearest = new Point(px, py); }
            }
            if (bestDistance == Double.MaxValue) throw new InvalidOperationException("Nenhum monitor disponível para o movimento do cursor.");
            return nearest;
        }

        public static bool Move(Point from, Point target, Rectangle[] screens, Random random,
            Func<long, bool> waitUntilMicroseconds, Action<Point> move, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            target = NearestScreenPoint(target.X, target.Y, screens);
            double dx = target.X - (double)from.X, dy = target.Y - (double)from.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance == 0) return true;
            double durationMs = Math.Max(120, Math.Min(850, (120 + Math.Sqrt(distance) * 14) * (0.9 + random.NextDouble() * 0.2)));
            int frames = Math.Max(1, (int)Math.Ceiling(durationMs / 8));
            double bend = (random.NextDouble() * 2 - 1) * Math.Min(30, distance * 0.06);
            double first = 0.25 + random.NextDouble() * 0.1;
            double second = 0.65 + random.NextDouble() * 0.1;
            double c1x = from.X + dx * first - dy / distance * bend;
            double c1y = from.Y + dy * first + dx / distance * bend;
            double c2x = from.X + dx * second - dy / distance * bend;
            double c2y = from.Y + dy * second + dx / distance * bend;
            Point previous = from;
            for (int frame = 1; frame <= frames; frame++)
            {
                cancellation.ThrowIfCancellationRequested();
                long timeUs = (long)Math.Round(durationMs * 1000 * frame / frames);
                if (!waitUntilMicroseconds(timeUs)) return false;
                cancellation.ThrowIfCancellationRequested();
                double t = frame / (double)frames;
                // Smooth acceleration and braking, with zero endpoint velocity.
                double u = t * t * t * (t * (t * 6 - 15) + 10);
                double v = 1 - u;
                Point point = frame == frames ? target : NearestScreenPoint(
                    (long)Math.Round(v*v*v*from.X + 3*v*v*u*c1x + 3*v*u*u*c2x + u*u*u*target.X),
                    (long)Math.Round(v*v*v*from.Y + 3*v*v*u*c1y + 3*v*u*u*c2y + u*u*u*target.Y), screens);
                if (point == previous) continue;
                move(point);
                previous = point;
            }
            return true;
        }
    }
}
