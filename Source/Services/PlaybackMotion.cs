using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace MacroDeck
{
    // One instance per cycle: all variation is relative to the saved recording,
    // never to the output of the previous cycle. Saved events remain untouched.
    internal sealed class PlaybackVariation
    {
        private readonly Rectangle[] screens;
        private readonly int offsetX, offsetY;
        private long actionDelay;
        private bool hasAction;

        public PlaybackVariation(int variationPixels, Random random, Rectangle[] screens)
        {
            this.screens = screens;
            if (variationPixels > 0)
            {
                offsetX = random.Next(-variationPixels, variationPixels + 1);
                offsetY = random.Next(-variationPixels, variationPixels + 1);
            }
        }

        public long WithActionDelay(long timestampUs, string kind, bool hasHeldInput, int maxDelayMs, Random random)
        {
            if (maxDelayMs == 0) return timestampUs;
            if (kind == "keyDown" || kind == "mouseDown" || kind == "wheel")
            {
                if (hasAction && !hasHeldInput)
                    actionDelay += random.Next(0, maxDelayMs + 1) * 1000L;
                hasAction = true;
            }
            return timestampUs > Int64.MaxValue - actionDelay ? Int64.MaxValue : timestampUs + actionDelay;
        }
        public Point Position(Point original)
        {
            if (offsetX == 0 && offsetY == 0) return original;
            Point shifted = PlaybackMotion.NearestScreenPoint((long)original.X + offsetX, (long)original.Y + offsetY, screens);
            if (Math.Abs((long)shifted.X - original.X) <= Math.Abs(offsetX) &&
                Math.Abs((long)shifted.Y - original.Y) <= Math.Abs(offsetY))
                return shifted;

            // At the edge of a monitor, keep a recorded on-screen point within the offset.
            foreach (Rectangle screen in screens)
                if (screen.Contains(original)) return original;

            return shifted;
        }

        public MacroEvent Event(MacroEvent original)
        {
            if ((offsetX == 0 && offsetY == 0) || !IsMouse(original.Kind)) return original;
            Point point = Position(new Point(original.X, original.Y));
            return new MacroEvent {
                Kind = original.Kind, Code = original.Code, X = point.X, Y = point.Y,
                Wheel = original.Wheel, TimeMs = original.TimeMs, TimeUs = original.TimeUs
            };
        }

        private static bool IsMouse(string kind)
        {
            return kind == "move" || kind == "mouseDown" || kind == "mouseUp" || kind == "wheel";
        }
    }

    internal sealed class CyclePauseSchedule
    {
        public const int DefaultChancePercent = 25;
        public const int DefaultMinutes = 5;
        public const int DefaultMinimumCycles = 3;

        private readonly int chancePercent;
        private readonly int minutes;
        private readonly int minimumCycles;
        private int cyclesUntilEligible;

        public CyclePauseSchedule(int chancePercent, int minutes, int minimumCycles)
        {
            this.chancePercent = chancePercent;
            this.minutes = minutes;
            this.minimumCycles = minimumCycles;
        }

        public static int ReadChancePercent(int? percent, int? legacyDenominator)
        {
            if (percent.HasValue) return percent.Value;
            if (!legacyDenominator.HasValue || legacyDenominator.Value < 2) return DefaultChancePercent;
            // The old 1/N field may give a fractional percentage.
            return Math.Max(1, (int)Math.Round(100.0 / legacyDenominator.Value, MidpointRounding.AwayFromZero));
        }

        public int CompleteCycle(Random random)
        {
            if (minutes == 0) return 0;
            if (cyclesUntilEligible > 0) cyclesUntilEligible--;
            if (cyclesUntilEligible > 0 || random.Next(0, 100) >= chancePercent) return 0;
            cyclesUntilEligible = minimumCycles;
            return minutes;
        }
    }

    internal static class PlaybackMotion
    {
        private const int ClickTargetTolerance = 4;

        public static long GetCyclePauseMicroseconds(int intervalSeconds, int variationSeconds, Random random)
        {
            int extraMs = variationSeconds > 0 ? random.Next(0, variationSeconds * 1000 + 1) : 0;
            return intervalSeconds * 1000000L + extraMs * 1000L;
        }

        public static IEnumerable<MacroEvent> VaryClicks(List<MacroEvent> events, int percent, Random random, CancellationToken cancellation)
        {
            var heldKeys = new HashSet<int>();
            var heldButtons = new HashSet<int>();
            for (int i = 0; i < events.Count;)
            {
                cancellation.ThrowIfCancellationRequested();
                MacroEvent item = events[i];
                if (heldKeys.Count == 0 && heldButtons.Count == 0 && IsClickPair(events, i, item))
                {
                    int end = i + 2;
                    while (IsClickPair(events, end, item) &&
                        GetEventTimeMicroseconds(events[end]) >= GetEventTimeMicroseconds(events[end - 1]))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        end += 2;
                    }

                    int count = (end - i) / 2;
                    int limit = (int)((long)count * percent / 100);
                    int delta = count > 1 && limit > 0 ? random.Next(-limit, limit + 1) : 0;
                    if (delta <= 0)
                    {
                        for (int j = i; j < end + 2 * delta; j++)
                            yield return events[j];
                        if (delta < 0 && end == events.Count)
                            yield return new MacroEvent { Kind = "wait", TimeUs = GetEventTimeMicroseconds(events[end - 1]) };
                    }
                    else
                    {
                        foreach (MacroEvent click in AddClicks(events, i, end, delta, cancellation))
                            yield return click;
                    }
                    i = end;
                    continue;
                }

                if (item != null)
                {
                    if (item.Kind == "keyDown") heldKeys.Add(item.Code);
                    if (item.Kind == "keyUp") heldKeys.Remove(item.Code);
                    if (item.Kind == "mouseDown") heldButtons.Add(item.Code);
                    if (item.Kind == "mouseUp") heldButtons.Remove(item.Code);
                }
                yield return item;
                i++;
            }
        }

        private static bool IsClickPair(List<MacroEvent> events, int index, MacroEvent target)
        {
            if (target == null || target.Kind != "mouseDown" || index >= events.Count - 1) return false;
            MacroEvent down = events[index], up = events[index + 1];
            return down != null && up != null && down.Kind == "mouseDown" && up.Kind == "mouseUp" &&
                down.Code == target.Code && up.Code == target.Code &&
                Math.Abs((long)down.X - target.X) <= ClickTargetTolerance &&
                Math.Abs((long)down.Y - target.Y) <= ClickTargetTolerance &&
                down.X == up.X && down.Y == up.Y &&
                GetEventTimeMicroseconds(up) >= GetEventTimeMicroseconds(down);
        }

        private static IEnumerable<MacroEvent> AddClicks(List<MacroEvent> events, int start, int end, int remaining, CancellationToken cancellation)
        {
            // Use recorded gaps so extra clicks don't shift subsequent recorded actions.
            const long minimumSpacingUs = 20000;
            for (int i = start; i < end; i += 2)
            {
                MacroEvent down = events[i], up = events[i + 1];
                yield return down;
                yield return up;
                if (remaining == 0 || i + 2 >= end) continue;
                long upTime = GetEventTimeMicroseconds(up);
                long gap = GetEventTimeMicroseconds(events[i + 2]) - upTime;
                long hold = Math.Max(minimumSpacingUs, GetEventTimeMicroseconds(up) - GetEventTimeMicroseconds(down));
                while (remaining > 0 && gap - 2 * minimumSpacingUs >= hold)
                {
                    cancellation.ThrowIfCancellationRequested();
                    upTime += minimumSpacingUs;
                    yield return CopyClickAt(down, upTime);
                    upTime += hold;
                    yield return CopyClickAt(up, upTime);
                    gap -= minimumSpacingUs + hold;
                    remaining--;
                }
            }
        }

        private static MacroEvent CopyClickAt(MacroEvent original, long timestampUs)
        {
            return new MacroEvent {
                Kind = original.Kind, Code = original.Code, X = original.X, Y = original.Y,
                TimeUs = timestampUs, TimeMs = timestampUs / 1000
            };
        }

        public static long GetEventTimeMicroseconds(MacroEvent item)
        {
            if (item.TimeUs.HasValue) return Math.Max(0L, item.TimeUs.Value);
            long milliseconds = Math.Max(0L, item.TimeMs);
            return milliseconds > Int64.MaxValue / 1000L ? Int64.MaxValue : milliseconds * 1000L;
        }

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
            double durationMs = Math.Max(75, Math.Min(850, (72 + Math.Sqrt(distance) * 14) * (0.88 + random.NextDouble() * 0.24)));
            int frames = Math.Max(1, (int)Math.Ceiling(durationMs / 8));
            double normalX = -dy / distance, normalY = dx / distance;
            double firstBend = (random.NextDouble() * 2 - 1) * Math.Min(30, distance * 0.06);
            double secondBend = firstBend * 0.45 + (random.NextDouble() * 2 - 1) * Math.Min(12, distance * 0.024);
            double first = 0.25 + random.NextDouble() * 0.1;
            double second = 0.65 + random.NextDouble() * 0.1;
            double c1x = from.X + dx * first + normalX * firstBend;
            double c1y = from.Y + dy * first + normalY * firstBend;
            double c2x = from.X + dx * second + normalX * secondBend;
            double c2y = from.Y + dy * second + normalY * secondBend;
            double speedBias = (random.NextDouble() * 2 - 1) * 0.18;
            double speedPhase = random.NextDouble() * 2 * Math.PI;
            double correctionPhase = random.NextDouble() * 2 * Math.PI;
            double correctionSize = Math.Min(3, distance * 0.012);
            Point previous = from;
            for (int frame = 1; frame <= frames; frame++)
            {
                cancellation.ThrowIfCancellationRequested();
                long timeUs = (long)Math.Round(durationMs * 1000 * frame / frames);
                if (!waitUntilMicroseconds(timeUs)) return false;
                cancellation.ThrowIfCancellationRequested();
                double t = frame / (double)frames;
                // Vary the pace through the move while keeping progress monotonic.
                t += t * (1 - t) * (speedBias + 0.06 * Math.Sin(2 * Math.PI * t + speedPhase));
                // Smooth acceleration and braking, with zero endpoint velocity.
                double u = t * t * t * (t * (t * 6 - 15) + 10);
                double v = 1 - u;
                // A small lateral adjustment fades in and out with the movement.
                double correction = correctionSize * 16 * u * u * v * v * Math.Sin(2 * Math.PI * u + correctionPhase);
                Point point = frame == frames ? target : NearestScreenPoint(
                    (long)Math.Round(v*v*v*from.X + 3*v*v*u*c1x + 3*v*u*u*c2x + u*u*u*target.X + normalX * correction),
                    (long)Math.Round(v*v*v*from.Y + 3*v*v*u*c1y + 3*v*u*u*c2y + u*u*u*target.Y + normalY * correction), screens);
                if (point == previous) continue;
                move(point);
                previous = point;
            }
            return true;
        }
    }
}
