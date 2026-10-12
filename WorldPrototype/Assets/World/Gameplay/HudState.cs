namespace EchoWorld
{
    // One modal inventory view, with optional chat focus above it. No Unity clock
    // lives here: WorldHud applies the pause state and owns the input-frame latch.
    public sealed class HudState
    {
        public bool IsTyping { get; private set; }
        public bool IsInventoryOpen { get; private set; }
        public bool IsChestOpen { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsHelpOpen { get; private set; }
        public bool BlocksGameplay => IsTyping || IsInventoryOpen || IsPaused;

        public bool BeginChat()
        {
            if (IsPaused || IsTyping) return false;
            IsTyping = true;
            return true;
        }

        public void EndChat() { IsTyping = false; }

        public bool ToggleInventory()
        {
            if (IsPaused || IsTyping) return false;
            IsInventoryOpen = !IsInventoryOpen;
            IsChestOpen = false;
            return true;
        }

        public bool OpenChest()
        {
            if (IsPaused || IsTyping) return false;
            IsInventoryOpen = IsChestOpen = true;
            return true;
        }

        public void HandleEscape()
        {
            if (IsTyping) EndChat();
            else if (IsInventoryOpen) IsInventoryOpen = IsChestOpen = false;
            else if (IsHelpOpen) IsHelpOpen = false;
            else IsPaused = !IsPaused;
        }

        public bool ShowHelp()
        {
            if (!IsPaused) return false;
            IsHelpOpen = true;
            return true;
        }

        public void Resume() { IsPaused = IsHelpOpen = false; }
    }
}
