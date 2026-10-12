using UnityEngine;

namespace EchoWorld
{
    [DefaultExecutionOrder(-200)]
    public sealed class WorldHud : MonoBehaviour
    {
        public PrototypeGame Game;
        public bool IsTyping => state.IsTyping;
        public bool IsInventoryOpen => state.IsInventoryOpen;
        public bool IsPaused => state.IsPaused;
        // Closing a view must not also send its key to the player's Update.
        public bool BlocksGameplay => state.BlocksGameplay || consumedFrame == Time.frameCount;

        readonly HudState state = new HudState();
        Vector3Int chestCell;
        string chat = "", transferNotice = "", lastNotice;
        int consumedFrame = -1, focusFrame = -1;
        bool requestFocus, clearFocus;
        float noticeUntil;
        GUIStyle label, small, title, centered, tinyCentered, right, field, button, bubble;
        const string ChatControl = "hud-local-chat";

        float Scale => Mathf.Max(.1f, Mathf.Min(2f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f)));
        float Width => Screen.width / Scale;
        float Height => Screen.height / Scale;
        Rect VitalsRect => new Rect(14, 14, 266, 104);
        Rect ClockRect => new Rect(Width - 162, 14, 148, 40);
        Rect HotbarRect => new Rect((Width - 704) / 2, Height - 86, 704, 72);
        Rect ChatRect => new Rect(18, Height - 272, 398, 164);
        Rect CenteredRect(float width, float height) => new Rect((Width - width) / 2, (Height - height) / 2, width, height);
        bool HasRecentChat => Game && Game.Session != null && Game.Session.LastMessage.Length > 0
            && Time.unscaledTime < Game.Session.MessageExpires;

        void Awake() { Time.timeScale = 1; }

        void Changed()
        {
            consumedFrame = Time.frameCount;
            Time.timeScale = IsPaused ? 0 : 1;
        }

        public void BeginChat()
        {
            if (!state.BeginChat()) return;
            focusFrame = Time.frameCount;
            requestFocus = true;
            clearFocus = false;
            Changed();
        }

        public void HandleEscape()
        {
            if (IsTyping)
            {
                chat = "";
                requestFocus = false;
                clearFocus = true;
            }
            state.HandleEscape();
            Changed();
        }

        public void ToggleInventory()
        {
            if (!state.ToggleInventory()) return;
            transferNotice = "";
            clearFocus = true;
            Changed();
        }

        public void OpenChest(Vector3Int cell)
        {
            if (!state.OpenChest()) return;
            chestCell = cell;
            transferNotice = "";
            clearFocus = true;
            Changed();
        }

        public void SubmitChat(string text)
        {
            if (IsPaused || !Game || Game.Session == null) return;
            // Session owns history, sanitization, the 140-character bound and expiry.
            // All text styles have richText disabled, including the speech bubble.
            if (!Game.Session.Say(text, Time.unscaledTime)) return;
            chat = "";
            state.EndChat();
            requestFocus = false;
            clearFocus = true;
            Changed();
        }

        void Resume()
        {
            state.Resume();
            clearFocus = true;
            Changed();
        }

        public void RestartGame()
        {
            if (!Game || !Game.Player) return;
            Game.Player.Respawn();
            Resume();
            Game.Save();
        }

        public bool ConsumesPointer(Vector3 screen)
        {
            if (BlocksGameplay) return true;
            Vector2 point = new Vector2(screen.x / Scale, (Screen.height - screen.y) / Scale);
            return VitalsRect.Contains(point) || ClockRect.Contains(point) || HotbarRect.Contains(point)
                || (HasRecentChat && ChatRect.Contains(point));
        }

        void Update()
        {
            Time.timeScale = IsPaused ? 0 : 1;
            if (Input.GetKeyDown(KeyCode.Escape)) { HandleEscape(); return; }
            if (IsPaused || IsTyping) return;
            if (Input.GetKeyDown(KeyCode.T)) { BeginChat(); return; }
            if (Input.GetKeyDown(KeyCode.E)) { ToggleInventory(); return; }
            if (IsInventoryOpen || !Game || !Game.Player) return;
            for (int digit = 0; digit <= 9; digit++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + digit)))
                    SelectItem((byte)(digit == 0 ? 10 : digit));

            string notice = Game.Player.Notice;
            if (notice != lastNotice)
            {
                // Do not turn the player's initial navigation notice into a HUD guide.
                if (lastNotice != null) noticeUntil = Time.unscaledTime + 5;
                lastNotice = notice;
            }
        }

        void SelectItem(byte id)
        {
            if (!Game || !Game.Player) return;
            Game.Player.SelectedBlock = id;
            Game.Player.Building = true;
            Changed();
        }

        void OnDisable()
        {
            state.Resume();
            state.EndChat();
            requestFocus = false;
            clearFocus = true;
            Time.timeScale = 1;
        }

        void OnDestroy() { Time.timeScale = 1; }

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, richText = false, padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = HudPaint.Ink }, clipping = TextClipping.Clip
            };
            small = new GUIStyle(label) { fontSize = 12, normal = { textColor = HudPaint.Muted } };
            title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            tinyCentered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            right = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            button = new GUIStyle(centered) { fontStyle = FontStyle.Bold };
            button.hover.textColor = HudPaint.Ink;
            button.active.textColor = HudPaint.Gold;
            button.focused.textColor = HudPaint.Gold;
            field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14, richText = false, padding = new RectOffset(7, 7, 5, 4),
                border = new RectOffset(), alignment = TextAnchor.MiddleLeft
            };
            foreach (GUIStyleState appearance in new[] { field.normal, field.hover, field.active, field.focused })
            {
                appearance.background = null;
                appearance.textColor = HudPaint.Ink;
            }
            bubble = new GUIStyle(centered)
            {
                wordWrap = true, padding = new RectOffset(12, 12, 8, 8),
                normal = { textColor = HudPaint.Well }
            };
        }

        void Text(Rect rect, string text, GUIStyle style = null)
        {
            GUI.Label(rect, text ?? "", style ?? label);
        }

        bool Button(Rect rect, string text, bool active = true)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && active;
            string control = "hud-button-" + text;
            bool highlighted = GUI.enabled && (rect.Contains(Event.current.mousePosition)
                || GUI.GetNameOfFocusedControl() == control);
            HudPaint.Panel(rect, highlighted);
            Color previousColor = GUI.color;
            if (!GUI.enabled) GUI.color = new Color(.60f, .60f, .60f, 1);
            GUI.SetNextControlName(control);
            bool clicked = GUI.Button(rect, text, button);
            GUI.color = previousColor;
            GUI.enabled = previous;
            return clicked;
        }

        void OnGUI()
        {
            if (!Game || Game.Session == null || !Game.Player) return;
            Styles();
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            try
            {
                if (clearFocus) { GUI.FocusControl(null); clearFocus = false; }
                HandleTextKeys();
                DrawAvatarText();
                DrawVitals();
                DrawClock();
                GUI.enabled = previousEnabled && !state.BlocksGameplay;
                DrawHotbar();
                GUI.enabled = previousEnabled;
                if (IsInventoryOpen)
                {
                    HudPaint.Fill(new Rect(0, 0, Width, Height), new Color(0, 0, 0, .38f));
                    GUI.enabled = previousEnabled && !IsTyping;
                    if (state.IsChestOpen) DrawChest(); else DrawInventory();
                    GUI.enabled = previousEnabled;
                }
                if (!IsPaused && (IsTyping || (!IsInventoryOpen && HasRecentChat))) DrawChat();
                if (IsPaused) DrawPause();
                if (state.BlocksGameplay && (Event.current.isMouse || Event.current.type == EventType.ScrollWheel))
                    Event.current.Use();
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
            }
        }

        void HandleTextKeys()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown) return;
            // Escape runs once in Update, before gameplay. Opening T is not chat text.
            if (current.keyCode == KeyCode.Escape || (IsTyping && Time.frameCount == focusFrame
                && (current.keyCode == KeyCode.T || current.character == 't' || current.character == 'T')))
                current.Use();
            else if (IsTyping && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            {
                if (Time.frameCount > focusFrame) SubmitChat(chat);
                current.Use();
            }
        }

        void DrawVitals()
        {
            Rect r = VitalsRect;
            HudPaint.Panel(r);
            int health = Mathf.Clamp(Game.Session.Health, 0, LocalSession.MaxHealth);
            float stamina = Mathf.Clamp(Game.Session.Stamina, 0, LocalSession.MaxStamina);
            HudPaint.Heart(new Rect(r.x + 12, r.y + 9, 70, 60), health / (float)LocalSession.MaxHealth);
            Text(new Rect(r.x + 8, r.y + 71, 80, 23), health + "/" + LocalSession.MaxHealth, centered);
            Text(new Rect(r.x + 101, r.y + 10, 60, 24), "Lv 1", title);
            HudPaint.Lock(r.x + 173, r.y + 15);
            Text(new Rect(r.x + 190, r.y + 13, 66, 20), "XP 0", small);
            Text(new Rect(r.x + 101, r.y + 37, 146, 19), "Stamina", small);
            Text(new Rect(r.x + 164, r.y + 35, 81, 21), Mathf.CeilToInt(stamina) + "/" + LocalSession.MaxStamina, right);
            HudPaint.Bar(new Rect(r.x + 101, r.y + 59, 146, 12), stamina / LocalSession.MaxStamina, HudPaint.Stamina);
            Text(new Rect(r.x + 101, r.y + 79, 146, 18), "Nivel y XP bloqueados", small);
        }

        void DrawClock()
        {
            if (Game.DayNight == null) return;
            Rect r = ClockRect;
            HudPaint.Panel(r);
            HudPaint.Fill(new Rect(r.x + 14, r.y + 12, 14, 14), Game.DayNight.IsDay ? HudPaint.Gold : HudPaint.Steel);
            if (!Game.DayNight.IsDay) HudPaint.Fill(new Rect(r.x + 20, r.y + 9, 10, 14), HudPaint.Wood);
            Text(new Rect(r.x + 36, r.y + 7, r.width - 45, 26), Game.DayNight.TimeLabel, centered);
        }

        void DrawHotbar()
        {
            Rect bar = HotbarRect;
            for (byte id = 1; id <= 10; id++)
            {
                Rect slot = new Rect(bar.x + 5 + (id - 1) * 70, bar.y + 4, 64, 64);
                if (Slot(slot, id, Game.Session.Count(id), "bar", false)) SelectItem(id);
            }
            if (state.BlocksGameplay) return;
            string selection = LocalSession.Name(Game.Player.SelectedBlock);
            if (Game.Player.Building) selection += " · Construir";
            Text(new Rect(bar.x, bar.y - 25, bar.width, 20), selection, tinyCentered);
            if (!string.IsNullOrEmpty(lastNotice) && Time.unscaledTime < noticeUntil)
            {
                Rect notice = new Rect((Width - 520) / 2, bar.y - 66, 520, 31);
                HudPaint.Panel(notice);
                Text(HudPaint.Inset(notice, 6), lastNotice, tinyCentered);
            }
        }

        bool Slot(Rect rect, byte id, int count, string owner, bool showName, bool active = true)
        {
            bool selected = Game.Player.SelectedBlock == id;
            HudPaint.Panel(rect, selected);
            HudPaint.Fill(HudPaint.Inset(rect, 5), HudPaint.Well);
            float imageY = rect.y + 11;
            HudPaint.Item(new Rect(rect.center.x - 16, imageY, 32, 32), id);
            Text(new Rect(rect.x + 7, rect.y + 3, 18, 18), id == 10 ? "0" : id.ToString(), small);
            Text(new Rect(rect.x + 8, rect.y + 44, rect.width - 16, 16), count.ToString(), right);
            if (showName) Text(new Rect(rect.x + 5, rect.yMax - 20, rect.width - 10, 16), LocalSession.Name(id), tinyCentered);
            if (selected) HudPaint.Fill(new Rect(rect.x + 7, rect.yMax - 5, rect.width - 14, 2), HudPaint.Gold);
            bool previous = GUI.enabled;
            GUI.enabled = previous && active;
            GUI.SetNextControlName("hud-slot-" + owner + "-" + id);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.enabled = previous;
            return clicked;
        }

        void ModalTitle(Rect rect, string text)
        {
            HudPaint.Panel(rect);
            Text(new Rect(rect.x + 24, rect.y + 16, rect.width - 90, 28), text, title);
            if (Button(new Rect(rect.xMax - 52, rect.y + 13, 32, 30), "X")) HandleEscape();
            HudPaint.Fill(new Rect(rect.x + 24, rect.y + 54, rect.width - 48, 2), HudPaint.Steel);
        }

        void DrawInventory()
        {
            Rect r = CenteredRect(672, 414);
            ModalTitle(r, "Inventario");
            for (byte id = 1; id <= 10; id++)
            {
                int index = id - 1;
                Rect slot = new Rect(r.x + 28 + index % 5 * 126, r.y + 74 + index / 5 * 98, 112, 86);
                if (Slot(slot, id, Game.Session.Count(id), "inventory", true)) SelectItem(id);
            }
            Text(new Rect(r.x + 28, r.y + 283, 400, 24), "Recetas", label);
            Text(new Rect(r.x + 28, r.y + 311, 614, 23), "1 madera + 1 piedra → 4 antorchas", small);
            Text(new Rect(r.x + 28, r.y + 338, 614, 23), "C para fabricar al volver al mundo.", small);
            HudPaint.Lock(r.x + 29, r.y + 376);
            Text(new Rect(r.x + 48, r.y + 372, 590, 22), "Lv 1 · XP bloqueada en Beta 0.0.2", small);
        }

        void DrawChest()
        {
            Rect r = CenteredRect(760, 382);
            ModalTitle(r, "Cofre");
            Text(new Rect(r.x + 24, r.y + 67, 340, 22), "Cofre · tomar uno", small);
            Text(new Rect(r.x + 396, r.y + 67, 340, 22), "Inventario · guardar uno", small);
            for (byte id = 1; id <= 10; id++)
            {
                int index = id - 1;
                int stored = Game.Chests == null ? 0 : Game.Chests.Count(chestCell, id);
                Rect left = new Rect(r.x + 24 + index % 5 * 68, r.y + 99 + index / 5 * 91, 64, 82);
                Rect rightSlot = new Rect(left.x + 372, left.y, 64, 82);
                if (Slot(left, id, stored, "chest", true, stored > 0 && Game.Chests != null)) Transfer(id, false);
                if (Slot(rightSlot, id, Game.Session.Count(id), "deposit", true, Game.Session.Count(id) > 0 && Game.Chests != null))
                    Transfer(id, true);
            }
            Text(new Rect(r.x + 24, r.y + 293, r.width - 48, 23), "Cada clic mueve una unidad.", small);
            Text(new Rect(r.x + 24, r.y + 329, r.width - 48, 28),
                Game.Chests == null ? "Cofre no disponible." : transferNotice, small);
        }

        void Transfer(byte id, bool deposit)
        {
            if (Game.Chests == null || IsTyping || IsPaused) return;
            // The service owns the atomic transfer. Never Take/Add a second time here.
            bool moved = deposit ? Game.Chests.DepositOne(chestCell, id) : Game.Chests.TakeOne(chestCell, id);
            transferNotice = moved ? (deposit ? "Guardado: " : "Recogido: ") + LocalSession.Name(id) + " ×1."
                : "No se pudo mover: destino lleno o cofre no disponible.";
            Changed();
        }

        void DrawChat()
        {
            Rect r = ChatRect;
            HudPaint.Panel(r);
            Text(new Rect(r.x + 12, r.y + 9, 240, 20), "Chat local", small);
            if (IsTyping) Text(new Rect(r.xMax - 81, r.y + 7, 68, 22), chat.Length + "/140", right);
            int row = 0;
            foreach (string message in Game.Session.ChatHistory)
                Text(new Rect(r.x + 12, r.y + 34 + row++ * 19, r.width - 24, 19), "Tú: " + message, small);
            if (!IsTyping) return;
            Rect input = new Rect(r.x + 12, r.yMax - 42, r.width - 88, 29);
            HudPaint.Fill(input, HudPaint.Edge);
            HudPaint.Fill(HudPaint.Inset(input, 1), HudPaint.Well);
            GUI.SetNextControlName(ChatControl);
            chat = GUI.TextField(input, chat, LocalSession.MaxChatLength, field);
            if (requestFocus && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(ChatControl);
                requestFocus = false;
            }
            if (Button(new Rect(r.xMax - 68, input.y, 56, input.height), "Enviar")) SubmitChat(chat);
        }

        void DrawPause()
        {
            HudPaint.Fill(new Rect(0, 0, Width, Height), new Color(0, 0, 0, .65f));
            if (state.IsHelpOpen) { DrawHelp(); return; }
            Rect r = CenteredRect(340, 396);
            HudPaint.Panel(r);
            Text(new Rect(r.x + 24, r.y + 23, r.width - 48, 32), "Pausa", title);
            float x = r.x + 24, y = r.y + 80, width = r.width - 48;
            if (Button(new Rect(x, y, width, 38), "Reanudar")) Resume();
            if (Button(new Rect(x, y + 48, width, 38), "Reiniciar partida")) RestartGame();
            if (Button(new Rect(x, y + 96, width, 38), "Ayuda")) { state.ShowHelp(); Changed(); }
            Button(new Rect(x, y + 144, width, 38), "Opciones", false);
            Button(new Rect(x, y + 192, width, 38), "Cambiar servidor", false);
            if (Button(new Rect(x, y + 252, width, 38), "Salir partida"))
            {
                Game.QuitGame();
            }
        }

        void DrawHelp()
        {
            Rect r = CenteredRect(608, 512);
            HudPaint.Panel(r);
            Text(new Rect(r.x + 26, r.y + 22, r.width - 52, 32), "Ayuda", title);
            string[] keys = { "WASD / clic", "Shift", "Espacio", "Q / Z", "Rueda", "E", "T / Enter", "1–9 / 0", "B", "Clic derecho", "C", "Esc" };
            string[] actions = { "Moverse / marcar destino", "Correr", "Salto manual", "Girar cámara / dirección contraria", "Acercar o alejar cámara", "Inventario / cerrar cofre", "Escribir en el chat / enviar", "Seleccionar las diez ranuras", "Activar o desactivar construcción", "Extraer material", "Fabricar 4 antorchas (1 madera + 1 piedra)", "Cerrar chat, inventario o pausa" };
            for (int i = 0; i < keys.Length; i++)
            {
                float y = r.y + 74 + i * 29;
                Text(new Rect(r.x + 28, y, 140, 23), keys[i], label);
                Text(new Rect(r.x + 182, y, r.width - 210, 23), actions[i], small);
            }
            if (Button(new Rect(r.x + 28, r.yMax - 60, r.width - 56, 36), "Volver a pausa")) HandleEscape();
        }

        void DrawAvatarText()
        {
            if (!Game.MainCamera || !HasRecentChat || state.BlocksGameplay) return;
            Vector3 point = Game.MainCamera.WorldToScreenPoint(Game.Player.transform.position + Vector3.up * 2.5f);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) return;
            point.x /= Scale;
            point.y = (Screen.height - point.y) / Scale;
            float width = Mathf.Clamp(Game.Session.LastMessage.Length * 7 + 24, 100, 310);
            float height = bubble.CalcHeight(new GUIContent(Game.Session.LastMessage), width);
            Rect r = new Rect(Mathf.Clamp(point.x - width / 2, 8, Width - width - 8),
                Mathf.Max(8, point.y - height - 12), width, height);
            HudPaint.Fill(r, HudPaint.Edge);
            HudPaint.Fill(HudPaint.Inset(r, 2), HudPaint.Ink);
            Text(r, Game.Session.LastMessage, bubble);
            float tail = Mathf.Clamp(point.x - 3, r.x + 6, r.xMax - 12);
            HudPaint.Fill(new Rect(tail, r.yMax, 8, 4), HudPaint.Edge);
            HudPaint.Fill(new Rect(tail + 2, r.yMax - 2, 4, 5), HudPaint.Ink);
        }
    }
}
