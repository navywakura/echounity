using System;
using System.Collections.Generic;

namespace EchoWorld.Editor
{
    // Pure state checks: callable by the editor runner or a standalone C# runner.
    public static class HudTests
    {
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static List<string> RunAll()
        {
            var passed = new List<string>();
            var hud = new HudState();
            Assert(!hud.BlocksGameplay, "World starts interactive");
            hud.ToggleInventory();
            Assert(hud.IsInventoryOpen && hud.BlocksGameplay && !hud.IsPaused,
                "Inventory blocks controls without pausing the world");
            hud.BeginChat();
            hud.HandleEscape();
            Assert(!hud.IsTyping && hud.IsInventoryOpen && !hud.IsPaused,
                "Escape closes focused chat before the inventory beneath it");
            hud.HandleEscape();
            Assert(!hud.IsInventoryOpen && !hud.IsPaused && !hud.BlocksGameplay,
                "Closing inventory does not also open pause");
            passed.Add("hud/escape-chat-inventory-priority");

            hud.OpenChest();
            Assert(hud.IsChestOpen && hud.IsInventoryOpen && hud.BlocksGameplay,
                "Chest is a blocking inventory view");
            hud.BeginChat();
            Assert(!hud.ToggleInventory() && !hud.OpenChest() && hud.IsChestOpen,
                "Typing cannot toggle inventory or switch chest");
            hud.HandleEscape();
            Assert(hud.IsChestOpen, "Cancelling chest chat preserves the chest");
            hud.ToggleInventory();
            Assert(!hud.IsInventoryOpen && !hud.IsChestOpen && !hud.BlocksGameplay,
                "E closes chest and inventory together");
            hud.OpenChest();
            hud.HandleEscape();
            Assert(!hud.IsChestOpen && !hud.IsPaused, "Escape closes chest before pause");
            passed.Add("hud/chest-and-typing-isolation");

            hud.HandleEscape();
            Assert(hud.IsPaused && hud.BlocksGameplay, "Escape from world pauses");
            Assert(!hud.BeginChat() && !hud.ToggleInventory() && !hud.OpenChest(),
                "Paused gameplay shortcuts cannot open another modal");
            hud.ShowHelp();
            Assert(hud.IsHelpOpen && hud.IsPaused, "Help belongs to pause");
            hud.HandleEscape();
            Assert(!hud.IsHelpOpen && hud.IsPaused, "Escape from help returns to pause");
            hud.HandleEscape();
            Assert(!hud.IsPaused && !hud.BlocksGameplay, "Escape resumes the world");
            Assert(!hud.ShowHelp(), "Help cannot open outside pause");
            passed.Add("hud/pause-help-resume");

            hud.BeginChat();
            Assert(hud.IsTyping && hud.BlocksGameplay && !hud.IsPaused,
                "Chat captures gameplay input without freezing time");
            hud.EndChat();
            Assert(!hud.IsTyping && !hud.BlocksGameplay, "Sending releases chat focus");
            hud.ToggleInventory();
            hud.BeginChat();
            hud.EndChat();
            Assert(hud.IsInventoryOpen && hud.BlocksGameplay,
                "Sending from inventory does not close the underlying modal");
            passed.Add("hud/chat-submit-preserves-underlying-view");

            hud.HandleEscape();
            hud.HandleEscape();
            hud.ShowHelp();
            hud.Resume();
            Assert(!hud.IsPaused && !hud.IsHelpOpen && !hud.BlocksGameplay,
                "Resume clears the pause subview");
            passed.Add("hud/resume-clears-help");
            return passed;
        }
    }
}
