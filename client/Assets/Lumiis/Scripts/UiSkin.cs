using UnityEngine;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Gemeinsame Helfer für alle UI-Toolkit-Oberflächen (Login, Charakterauswahl, HUD): einheitliche
    /// PanelSettings mit Bildschirm-Skalierung und das Aufspannen geklonter UXML-Bäume auf die volle
    /// Panelfläche. Kein Zustand - reine Fabrik-/Hilfsfunktionen.</summary>
    public static class UiSkin
    {
        /// <summary>Referenzauflösung, auf die alle UXML/USS-Maße ausgelegt sind. Größere/kleinere Fenster
        /// werden proportional skaliert, statt dass die Oberfläche in einer Ecke klebt oder abgeschnitten wird.</summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(1280, 720);

        /// <summary>Kleinster Skalierungsfaktor - darunter würde die Schrift bei kleinen Fenstern unlesbar.
        /// Nur wenn das Fenster so klein ist, dass die Oberfläche sonst nicht mehr passt, geht es tiefer.</summary>
        public const float MinReadableScale = 1f;

        /// <summary>Kleinste logische Fläche, die die Oberfläche mindestens braucht (Taskleiste + Fenster).</summary>
        private static readonly Vector2 MinLayoutSize = new Vector2(1000f, 600f);

        /// <summary>Erzeugt PanelSettings mit konstantem, von <see cref="ComputeScale"/> gesteuertem Faktor
        /// (siehe <see cref="UiScaler"/>). sortingOrder: höher = weiter vorn (Login/Escape-Menü liegt über
        /// dem HUD).</summary>
        public static PanelSettings CreatePanelSettings(float sortingOrder)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultRuntimeTheme");
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = ComputeScale(Screen.width, Screen.height);
            settings.sortingOrder = sortingOrder;
            return settings;
        }

        /// <summary>Wie "Scale With Screen Size" (geometrisches Mittel aus Breiten- und Höhenverhältnis zur
        /// Referenz 1280x720), aber nie kleiner als <see cref="MinReadableScale"/> - auf kleinen Fenstern
        /// bleibt die Schrift so in voller Referenzgröße statt mitzuschrumpfen. Nur wenn selbst das nicht mehr
        /// passt (Fenster unter 1000x600), wird so weit verkleinert, dass die Oberfläche noch hineinpasst.</summary>
        public static float ComputeScale(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0) return 1f;
            float wRatio = screenWidth / (float)ReferenceResolution.x;
            float hRatio = screenHeight / (float)ReferenceResolution.y;
            float scale = Mathf.Sqrt(wRatio * hRatio);
            scale = Mathf.Max(scale, MinReadableScale);
            float fit = Mathf.Min(screenWidth / MinLayoutSize.x, screenHeight / MinLayoutSize.y);
            return Mathf.Min(scale, fit);
        }

        /// <summary>CloneTree() liefert einen TemplateContainer, der als normales Flex-Kind nur so hoch wird
        /// wie sein Inhalt. Sind alle Kinder absolut positioniert (wie .screen), ist er 0 px hoch - die
        /// "zentrierte" Oberfläche landet dann halb oberhalb des Bildschirmrands. Deshalb jeden geklonten Baum
        /// absolut auf die volle Fläche spannen; Klicks gehen durch den Container selbst hindurch.</summary>
        public static void StretchToFill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
            element.pickingMode = PickingMode.Ignore;
        }

        /// <summary>Baut eine kleine Pixelart-Figur (Krieger, Blick nach unten) aus den echten Charakter-
        /// Sheets als übereinanderliegende UI-Bilder - für die Charakterauswahl. Fehlende Sheets werden
        /// einfach übersprungen.</summary>
        public static void BuildCharacterPortrait(VisualElement parent, float size)
        {
            parent.Clear();
            AddPortraitLayer(parent, "Characters/body/warrior_m", 0, size);
            AddPortraitLayer(parent, "Characters/armor/01_moenchsplattenpanzer", 0, size);
            AddPortraitLayer(parent, "Characters/head/warrior_m", 0, size);
            AddPortraitLayer(parent, "Characters/hair/warrior_m_1", 0, size);
            // Waffen-Sheet: Zeilen 3-5 sind die Vorderseite, Zeile 3 = Blick nach unten.
            AddPortraitLayer(parent, "Characters/weapon/schwert_02", 3, size);
        }

        private static void AddPortraitLayer(VisualElement parent, string resource, int row, float size)
        {
            const float cell = 48f;
            Texture2D texture = Resources.Load<Texture2D>(resource);
            if (texture == null) return;

            var image = new Image
            {
                image = texture,
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
                // uv: normiert, Ursprung unten links - Zeile 0 ist die oberste Zeile des Sheets.
                uv = new Rect(0f, (texture.height - (row + 1) * cell) / texture.height, cell / texture.width, cell / texture.height)
            };
            image.style.position = Position.Absolute;
            image.style.left = 0;
            image.style.top = 0;
            image.style.width = size;
            image.style.height = size;
            parent.Add(image);
        }
    }
}
