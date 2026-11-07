using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MareDeMentiras
{
    /// <summary>Peças de interface reutilizadas pela mesa e pelos menus (fontes, sprites, botões, animações).</summary>
    public static class UIKit
    {
        public static readonly Color Gold = new Color(1f, 0.83f, 0.40f);
        public static readonly Color Cream = new Color(0.98f, 0.94f, 0.85f);
        public static readonly Color Ink = new Color(0.22f, 0.14f, 0.08f);
        public static readonly Color Muted = new Color(0.72f, 0.68f, 0.60f);
        public static readonly Color Red = new Color(1f, 0.42f, 0.36f);
        public static readonly Color Green = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color Blue = new Color(0.45f, 0.78f, 1f);

        public static Font Title, Body, Bold;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sprites.Clear();
            Title = Body = Bold = null;
        }

        public static void Init()
        {
            if (Title != null) return;
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Title = Resources.Load<Font>("Fonts/PirataOne-Regular") ?? legacy;
            Body = Resources.Load<Font>("Fonts/AlegreyaSans-Regular") ?? legacy;
            Bold = Resources.Load<Font>("Fonts/AlegreyaSans-ExtraBold") ?? legacy;
        }

        public static RectTransform MakeCanvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        public static Image Background(Transform parent)
        {
            var bg = Img("Fundo", parent, Spr("Fundo"), Color.white);
            Place(bg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            var fit = bg.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;
            bg.raycastTarget = false;
            return bg;
        }

        /// <summary>Carrega Resources/{folder}/{name}.png como Sprite (9-slice se border > 0).</summary>
        public static Sprite Spr(string name, float border = 0, string folder = "UI")
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = folder + "/" + name + "#" + border;
            if (sprites.TryGetValue(key, out var sp) && sp != null) return sp;
            var tex = Resources.Load<Texture2D>(folder + "/" + name);
            sp = tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100, 0,
                                                    SpriteMeshType.FullRect, Vector4.one * border);
            sprites[key] = sp;
            return sp;
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color, bool sliced = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            if (sliced && sprite != null) img.type = Image.Type.Sliced;
            return img;
        }

        public static Text Txt(string name, Transform parent, string content, Font font, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button MakeButton(Transform parent, Option opt, UnityEngine.Events.UnityAction onClick)
        {
            string sprite = opt.style switch
            {
                ButtonStyle.Vermelho => "BotaoVermelho",
                ButtonStyle.Verde => "BotaoVerde",
                ButtonStyle.Azul => "BotaoAzul",
                _ => "Botao",
            };
            var img = Img("Botao", parent, Spr(sprite, 26), Color.white, sliced: true);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = new Color(0.9f, 0.9f, 0.9f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = new Color(0.9f, 0.9f, 0.9f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            cb.disabledColor = new Color(0.4f, 0.4f, 0.42f, 0.75f);
            b.colors = cb;
            b.onClick.AddListener(onClick);
            b.interactable = opt.enabled;
            AddShadow(img.gameObject, 4);
            if (opt.enabled) img.gameObject.AddComponent<Hover>().Setup(1.05f, 0);

            float left = 12;
            var icon = opt.icon != null ? Spr(opt.icon) : null;
            if (icon != null)
            {
                var ic = Img("Icone", img.rectTransform, icon, opt.enabled ? Color.white : new Color(1, 1, 1, 0.4f));
                ic.preserveAspect = true;
                ic.raycastTarget = false;
                Place(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(38, 1), new Vector2(52, 52));
                left = 70;
            }

            var textColor = opt.enabled ? Cream : new Color(0.8f, 0.8f, 0.8f, 0.55f);
            bool hasSub = !string.IsNullOrEmpty(opt.sub);
            var label = Txt("Texto", img.rectTransform, opt.label, Bold, 23, textColor,
                            icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(label.rectTransform, 0, hasSub ? 0.42f : 0, 1, 1, left, 0, -10, hasSub ? -4 : -3);
            AddShadow(label.gameObject, 1.5f, 0.8f);
            if (hasSub)
            {
                var sub = Txt("Sub", img.rectTransform, opt.sub, Body, 17, new Color(textColor.r, textColor.g, textColor.b, textColor.a * 0.85f),
                              icon != null ? TextAnchor.UpperLeft : TextAnchor.UpperCenter);
                Stretch(sub.rectTransform, 0, 0, 1, 0.48f, left, 4, -10, 0);
            }
            return b;
        }

        /// <summary>Campo de texto com fundo escuro e moldura dourada.</summary>
        public static InputField MakeInput(Transform parent, string placeholder, int limit, int fontSize = 30)
        {
            var bg = Img("Campo", parent, Spr("Painel", 22), Color.white, sliced: true);
            var input = bg.gameObject.AddComponent<InputField>();
            var text = Txt("Texto", bg.rectTransform, "", Bold, fontSize, Cream, TextAnchor.MiddleCenter);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform, 0, 0, 1, 1, 18, 4, -18, -4);
            var ph = Txt("Dica", bg.rectTransform, placeholder, Body, fontSize - 4, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleCenter);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 0, 0, 1, 1, 18, 4, -18, -4);
            input.textComponent = text;
            input.placeholder = ph;
            input.characterLimit = limit;
            input.caretColor = Gold;
            input.selectionColor = new Color(1f, 0.83f, 0.4f, 0.35f);
            input.customCaretColor = true;
            return input;
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt, float x0, float y0, float x1, float y1,
                                   float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(r, t);
        }

        public static void AddShadow(GameObject go, float dist, float alpha = 0.45f)
        {
            var s = go.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, alpha);
            s.effectDistance = new Vector2(dist * 0.6f, -dist);
        }

        public static void AddOutline(GameObject go, Color c, float dist)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(dist, -dist);
        }

        public static IEnumerator Tween(float dur, System.Action<float> f)
        {
            for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
            {
                f(Mathf.Clamp01(t / dur));
                yield return null;
            }
            f(1);
        }

        public static float BackOut(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1;
            return 1 + c3 * Mathf.Pow(k - 1, 3) + c1 * Mathf.Pow(k - 1, 2);
        }

        /// <summary>Remove marcadores que poderiam bagunçar textos (apelidos digitados pelos jogadores).</summary>
        public static string CleanName(string s, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s.Trim())
                if (ch != '{' && ch != '}' && ch != '<' && ch != '>' && !char.IsControl(ch)) sb.Append(ch);
            var r = sb.ToString().Trim();
            if (r.Length > 16) r = r.Substring(0, 16);
            return r.Length == 0 ? fallback : r;
        }
    }

    /// <summary>Aumenta (e levanta) o elemento quando o mouse passa por cima.</summary>
    public class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        float scale = 1.05f, lift;
        bool over;
        Vector2 basePos;
        RectTransform rt;

        public void Setup(float scale, float lift)
        {
            this.scale = scale;
            this.lift = lift;
            rt = (RectTransform)transform;
            basePos = rt.anchoredPosition;
        }

        public void OnPointerEnter(PointerEventData e) => over = true;
        public void OnPointerExit(PointerEventData e) => over = false;

        void Update()
        {
            if (rt == null) return;
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14f);
            var targetScale = Vector3.one * (over ? scale : 1f);
            if (rt.localScale.y > 0.01f && rt.localScale.x > 0.01f)
                rt.localScale = Vector3.Lerp(rt.localScale, targetScale, k);
            if (lift > 0)
            {
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, basePos + (over ? Vector2.up * lift : Vector2.zero), k);
                if (over) rt.SetAsLastSibling();
            }
        }
    }
}
