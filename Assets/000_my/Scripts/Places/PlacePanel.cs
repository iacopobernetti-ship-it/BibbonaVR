using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Artemis.Vr;

namespace Artemis.Places
{
    /// <summary>
    /// La scheda "Places" della HUD: un pulsante per l'hub e uno per ciascun luogo, generati da
    /// PlaceFlow — una fonte sola. Verde = scena attiva; durante un caricamento i pulsanti si
    /// disattivano e la riga di stato mostra il progresso, che e' il feedback che serve quando i
    /// tempi di caricamento degli splat non sono garantiti.
    ///
    /// SCORRIMENTO A PAGINE, per i siti con molti luoghi (Bibbona: 12 fra vie e piazze).
    /// I luoghi stanno su una griglia di Columns colonne; una pagina ne mostra Rows Per Page righe,
    /// e sotto compaiono Prev / pagina / Next. L'hub resta sempre in cima, fuori dalle pagine:
    /// e' il ritorno, deve essere raggiungibile da qualunque pagina.
    ///
    /// Perche' pagine e non una lista da trascinare (ScrollRect): in visore trascinare col raggio
    /// e' impreciso, e si finisce per premere il pulsante sotto il dito invece di scorrere. Con
    /// le pagine ogni gesto e' un clic. In piu' i pulsanti fuori pagina sono SPENTI, non solo
    /// nascosti da una maschera: il raggio, il mouse dell'Editor e il layout li ignorano.
    ///
    /// Con pochi luoghi (Caldana: 6, cioe' 3 righe da 2) c'e' una pagina sola e la riga Prev/Next
    /// non compare: la scheda e' identica a prima, solo su due colonne.
    ///
    /// Si apre sulla pagina che contiene la scena corrente.
    ///
    /// La scelta del luogo porta dentro tutti i partecipanti: decide chi ospita, e agli altri la
    /// scheda non esiste (PlaceFlow.CanCommand).
    ///
    /// Sta sullo stesso oggetto di PlaceFlow.
    /// </summary>
    public class PlacePanel : MonoBehaviour
    {
        [SerializeField] private string tabTitle = "Places";

        [Header("Griglia dei luoghi")]
        [Tooltip("Pulsanti per riga. 2 = due colonne: dimezza l'altezza della lista, e i nomi " +
                 "delle vie ci stanno ancora (il testo si restringe da solo se serve).")]
        [Min(1)] [SerializeField] private int columns = 2;
        [Tooltip("Righe per pagina. Con 2 colonne e 3 righe una pagina mostra 6 luoghi: Caldana " +
                 "ci sta in una pagina sola, Bibbona in due.")]
        [Min(1)] [SerializeField] private int rowsPerPage = 3;
        [Tooltip("Altezza delle righe (px). 40 si centra bene col raggio.")]
        [SerializeField] private float rowHeight = 40f;

        private readonly Dictionary<string, Image> buttonImages = new Dictionary<string, Image>();
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<RectTransform> placeRows = new List<RectTransform>();
        private RectTransform navRow;
        private TMP_Text pageLabel, status;
        private int page, pageCount = 1;
        private bool built;
        private float nextRefresh;

        private void Start() { TryBuild(); }

        private void Update()
        {
            if (!built) { TryBuild(); return; }
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.5f;
            RefreshColors();
        }

        private void OnDestroy()
        {
            var flow = PlaceFlow.Instance;
            if (flow == null) return;
            flow.OnLoadStarted -= OnLoadStarted;
            flow.OnLoadProgress -= OnLoadProgress;
            flow.OnLoadFinished -= OnLoadFinished;
        }

        // ---------------------------------------------------------------- costruzione

        /// HUD e flusso sono due componenti sullo stesso oggetto, ma l'ordine di Awake fra
        /// componenti non e' garantito: si costruisce appena ci sono entrambi, senza pretese sul
        /// primo frame.
        private void TryBuild()
        {
            var hud = VrHud.Instance;
            var flow = PlaceFlow.Instance;
            if (hud == null || flow == null) return;

            // La scelta del luogo porta dentro tutti i partecipanti: decide chi ospita. Agli altri
            // la scheda non esiste PROPRIO — un pulsante disabilitato inviterebbe comunque a
            // premerlo e a chiedersi perche'. Non si rinuncia per sempre, pero': la sessione
            // potrebbe non essere ancora in piedi, e fra un istante il ruolo sara' chiaro.
            if (!PlaceFlow.CanCommand) return;

            var page0 = hud.CreateTab(tabTitle);
            // Spaziatura ridotta solo per questa pagina: con l'hub, tre righe di luoghi, la riga
            // delle pagine e lo stato, la scheda sta nel pannello da 500 px anche con la fascia
            // diagnostica accesa. Il titolo "Choose where to go" non c'e' piu': lo dice la scheda.
            var vlg = page0.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.spacing = 6f;

            // L'hub, a tutta larghezza e sempre visibile.
            var hubRow = hud.MakeRow(page0, rowHeight);
            AddButton(hud, flow, hubRow, flow.Hub.sceneName, flow.Hub.Label);

            // I luoghi, a righe di 'columns'. L'ultima riga incompleta si chiude con spazi vuoti,
            // altrimenti l'ultimo pulsante si allargherebbe a tutta la riga.
            int cols = Mathf.Max(1, columns);
            RectTransform row = null;
            int inRow = 0;
            foreach (var p in flow.Places)
            {
                if (row == null || inRow == cols)
                {
                    row = hud.MakeRow(page0, rowHeight);
                    placeRows.Add(row);
                    inRow = 0;
                }
                AddButton(hud, flow, row, p.sceneName, p.Label);
                inRow++;
            }
            if (row != null)
                for (; inRow < cols; inRow++) AddSpacer(row);

            // Pagine.
            int rows = Mathf.Max(1, rowsPerPage);
            pageCount = Mathf.Max(1, (placeRows.Count + rows - 1) / rows);
            navRow = hud.MakeRow(page0, rowHeight);
            hud.MakeButton(navRow, "Prev", () => ShowPage(page - 1));
            pageLabel = hud.MakeLabel(navRow, "", 18);
            hud.MakeButton(navRow, "Next", () => ShowPage(page + 1));
            navRow.gameObject.SetActive(pageCount > 1);

            status = hud.MakeLabel(page0, "", 16);

            flow.OnLoadStarted += OnLoadStarted;
            flow.OnLoadProgress += OnLoadProgress;
            flow.OnLoadFinished += OnLoadFinished;

            built = true;
            ShowPage(PageOf(flow.CurrentScene));
            RefreshColors();
        }

        private void AddButton(VrHud hud, PlaceFlow flow, RectTransform row, string sceneName, string label)
        {
            string scene = sceneName;                    // copia locale per la closure
            var (btn, img) = hud.MakeButton(row, label, () => flow.GoToPlace(scene));
            buttonImages[scene] = img;
            buttons.Add(btn);
        }

        private static void AddSpacer(RectTransform row)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(row, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        // ---------------------------------------------------------------- pagine

        /// Mostra le righe di una pagina e spegne le altre. Le pagine girano: Next dall'ultima
        /// torna alla prima, cosi' non c'e' un pulsante che a volte non fa niente.
        private void ShowPage(int p)
        {
            page = ((p % pageCount) + pageCount) % pageCount;
            int rows = Mathf.Max(1, rowsPerPage);
            for (int i = 0; i < placeRows.Count; i++)
                placeRows[i].gameObject.SetActive(i / rows == page);
            if (pageLabel != null) pageLabel.text = $"{page + 1} / {pageCount}";
        }

        /// La pagina che contiene una scena: si apre li', cosi' il luogo corrente (verde) e' a vista.
        private int PageOf(string scene)
        {
            var flow = PlaceFlow.Instance;
            if (flow == null) return 0;
            int cols = Mathf.Max(1, columns), rows = Mathf.Max(1, rowsPerPage);
            for (int i = 0; i < flow.Places.Count; i++)
                if (flow.Places[i].sceneName == scene) return (i / cols) / rows;
            return 0;
        }

        // ---------------------------------------------------------------- eventi flusso

        private void OnLoadStarted(string scene)
        {
            foreach (var b in buttons) b.interactable = false;
            if (status != null) status.text = $"Loading {scene}…";
        }

        private void OnLoadProgress(float p)
        {
            if (status != null && PlaceFlow.Instance != null && PlaceFlow.Instance.IsBusy)
                status.text = $"Loading… {p:P0}";
        }

        private void OnLoadFinished(string scene)
        {
            foreach (var b in buttons) b.interactable = true;
            if (status != null)
                status.text = PlaceFlow.Instance != null && PlaceFlow.Instance.IsOnHub
                    ? ""
                    : "Place ready — the splat may take a few more seconds.";
            RefreshColors();
        }

        // ---------------------------------------------------------------- stato

        private void RefreshColors()
        {
            var hud = VrHud.Instance;
            var flow = PlaceFlow.Instance;
            if (hud == null || flow == null) return;

            string current = flow.CurrentScene;
            foreach (var kv in buttonImages)
                kv.Value.color = kv.Key == current ? hud.ActiveColor : hud.ButtonColor;
        }
    }
}
