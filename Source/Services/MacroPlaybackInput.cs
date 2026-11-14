using System;
using System.Collections.Generic;
using System.Threading;

namespace MacroDeck
{
    // Used by the single playback worker only. The UI requests cancellation without
    // blocking on native input (which can call the UI's keyboard hook synchronously).
    // MainForm keeps playback reserved until this worker has released held inputs.
    internal sealed class MacroPlaybackInput
    {
        private readonly CancellationToken cancellation;
        private readonly Action<MacroEvent> send;
        private readonly Action<MacroEvent> release;
        private readonly HashSet<int> keys = new HashSet<int>();
        private readonly HashSet<int> buttons = new HashSet<int>();

        public bool HasHeldInput { get { return keys.Count != 0 || buttons.Count != 0; } }

        public MacroPlaybackInput(CancellationToken cancellation, Action<MacroEvent> send, Action<MacroEvent> release)
        {
            this.cancellation = cancellation;
            this.send = send;
            this.release = release;
        }

        public void Send(MacroEvent item)
        {
            cancellation.ThrowIfCancellationRequested();
            // Track before sending: a native call can fail after accepting input.
            if (item.Kind == "keyDown") keys.Add(item.Code);
            if (item.Kind == "mouseDown") buttons.Add(item.Code);
            send(item);
            if (item.Kind == "keyUp") keys.Remove(item.Code);
            if (item.Kind == "mouseUp") buttons.Remove(item.Code);
        }

        public void ReleaseHeld()
        {
            Exception failure = null;
            ReleaseSet(keys, "keyUp", ref failure);
            ReleaseSet(buttons, "mouseUp", ref failure);
            if (failure != null) throw failure;
        }

        private void ReleaseSet(HashSet<int> held, string kind, ref Exception failure)
        {
            foreach (int code in new List<int>(held))
            {
                try
                {
                    release(new MacroEvent { Kind = kind, Code = code });
                    held.Remove(code);
                }
                catch (Exception ex)
                {
                    // Still attempt to release the other keys and buttons.
                    if (failure == null) failure = ex;
                }
            }
        }
    }
}
