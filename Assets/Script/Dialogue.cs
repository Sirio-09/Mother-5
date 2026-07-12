using UnityEngine;
using TMPro;
using System.Collections;

public class NuvolaAvanzata : MonoBehaviour
{
    [Header("Componenti UI")]
    public GameObject canvasNuvola;
    public RectTransform pannelloSfondo;
    public RectTransform codaFumetto;
    public TextMeshProUGUI testoFumetto;

    [Header("Impostazioni Dialogo")]
    [TextArea(3, 5)]
    public string[] messaggi;

    public float larghezzaMassima = 150f;
    public float altezzaMinima = 40f;
    public float paddingX = 20f;
    public float paddingY = 15f;
    public float velocitaTesto = 0.03f;
    public float offsetCoda = 5f;

    [Header("Animazioni (Juice)")]
    public float ampiezzaGalleggiamento = 2f; // Quanto va su e giù
    public float velocitaGalleggiamento = 4f; // Quanto velocemente va su e giù
    public float durataPopIn = 0.15f;         // Tempo per ingrandirsi all'inizio

    private bool giocatoreVicino;
    private Camera cam;

    private bool stoScrivendo;
    private Coroutine coroutineScrittura;
    private int indiceMessaggio = 0;

    private Vector3 scalaOriginaleCanvas;

    void Start()
    {
        cam = Camera.main;
        scalaOriginaleCanvas = canvasNuvola.transform.localScale; // Salviamo la scala per l'animazione
        canvasNuvola.SetActive(false);
    }

    void Update()
    {
        if (giocatoreVicino && Input.GetKeyDown(KeyCode.Z))
        {
            if (!canvasNuvola.activeSelf)
            {
                indiceMessaggio = 0;
                ApriFumetto();
                StartCoroutine(AnimazionePopIn()); // Fa rimbalzare la nuvoletta all'apertura
            }
            else if (stoScrivendo)
            {
                CompletaTesto();
            }
            else
            {
                if (indiceMessaggio < messaggi.Length - 1)
                {
                    indiceMessaggio++;
                    ApriFumetto();
                }
                else
                {
                    canvasNuvola.SetActive(false);
                }
            }
        }

        if (canvasNuvola.activeSelf)
        {
            MantieniPannelloNelloSchermo();
        }
    }

    void ApriFumetto()
    {
        canvasNuvola.SetActive(true);
        testoFumetto.text = "";

        pannelloSfondo.sizeDelta = new Vector2(paddingX, altezzaMinima + paddingY);

        if (coroutineScrittura != null) StopCoroutine(coroutineScrittura);
        coroutineScrittura = StartCoroutine(EffettoMacchinaDaScrivere());
    }

    // --- NUOVA COROUTINE: Ingrandisce dolcemente la nuvola partendo da 0 ---
    IEnumerator AnimazionePopIn()
    {
        float tempoTrascorso = 0f;
        canvasNuvola.transform.localScale = Vector3.zero;

        while (tempoTrascorso < durataPopIn)
        {
            tempoTrascorso += Time.deltaTime;
            // SmoothStep crea un'accelerazione e decelerazione molto naturale
            float t = Mathf.SmoothStep(0f, 1f, tempoTrascorso / durataPopIn);
            canvasNuvola.transform.localScale = Vector3.Lerp(Vector3.zero, scalaOriginaleCanvas, t);
            yield return null;
        }

        canvasNuvola.transform.localScale = scalaOriginaleCanvas;
    }

    IEnumerator EffettoMacchinaDaScrivere()
    {
        stoScrivendo = true;
        testoFumetto.text = "";
        float maxLarghezzaRaggiunta = 10f;

        foreach (char lettera in messaggi[indiceMessaggio].ToCharArray())
        {
            testoFumetto.text += lettera;
            testoFumetto.rectTransform.sizeDelta = new Vector2(larghezzaMassima, 1000f);
            testoFumetto.ForceMeshUpdate();

            Vector2 dimensioniParziali = testoFumetto.GetRenderedValues(false);

            if (dimensioniParziali.x > maxLarghezzaRaggiunta)
            {
                maxLarghezzaRaggiunta = Mathf.Min(dimensioniParziali.x, larghezzaMassima);
            }

            float altezzaAttuale = Mathf.Max(dimensioniParziali.y, altezzaMinima);

            testoFumetto.rectTransform.sizeDelta = new Vector2(maxLarghezzaRaggiunta, altezzaAttuale);
            pannelloSfondo.sizeDelta = new Vector2(maxLarghezzaRaggiunta + paddingX, altezzaAttuale + paddingY);

            yield return new WaitForSeconds(velocitaTesto);
        }
        stoScrivendo = false;
    }

    void CompletaTesto()
    {
        if (coroutineScrittura != null) StopCoroutine(coroutineScrittura);

        testoFumetto.text = messaggi[indiceMessaggio];
        testoFumetto.rectTransform.sizeDelta = new Vector2(larghezzaMassima, 1000f);
        testoFumetto.ForceMeshUpdate();

        Vector2 dimensioniFinali = testoFumetto.GetRenderedValues(false);
        float w = Mathf.Min(dimensioniFinali.x, larghezzaMassima);
        float h = Mathf.Max(dimensioniFinali.y, altezzaMinima);

        testoFumetto.rectTransform.sizeDelta = new Vector2(w, h);
        pannelloSfondo.sizeDelta = new Vector2(w + paddingX, h + paddingY);

        stoScrivendo = false;
    }

    void MantieniPannelloNelloSchermo()
    {
        float metaAltezzaCam = cam.orthographicSize;
        float metaLarghezzaCam = cam.aspect * metaAltezzaCam;
        float margine = 0.2f;

        float bordoSinistro = cam.transform.position.x - metaLarghezzaCam + margine;
        float bordoDestro = cam.transform.position.x + metaLarghezzaCam - margine;

        // --- CALCOLO ONDA SENOIDALE PER IL GALLEGGIAMENTO ---
        // Genera un valore che va su e giù nel tempo in modo morbido
        float offsetFluttuante = Mathf.Sin(Time.time * velocitaGalleggiamento) * ampiezzaGalleggiamento;

        // GESTIONE PANNELLO BIANCO
        Vector3 posPannello = canvasNuvola.transform.position;

        float altezzaPannelloMondo = pannelloSfondo.sizeDelta.y * canvasNuvola.transform.localScale.y;
        float metaLarghezzaPannelloMondo = (pannelloSfondo.sizeDelta.x * canvasNuvola.transform.localScale.x) / 2f;

        // Alziamo il pannello e aggiungiamo l'onda fluttuante
        posPannello.y += (altezzaPannelloMondo / 2f) + (offsetFluttuante * canvasNuvola.transform.localScale.y);
        posPannello.x = Mathf.Clamp(posPannello.x, bordoSinistro + metaLarghezzaPannelloMondo, bordoDestro - metaLarghezzaPannelloMondo);
        pannelloSfondo.position = posPannello;

        // GESTIONE CODA MAGNETICA
        Vector3 posCoda = codaFumetto.position;
        // Anche la coda fluttua seguendo il pannello
        posCoda.y = pannelloSfondo.position.y - (altezzaPannelloMondo / 2f) + (offsetCoda * canvasNuvola.transform.localScale.y);

        float limiteCoda = metaLarghezzaPannelloMondo - ((codaFumetto.sizeDelta.x * canvasNuvola.transform.localScale.x) / 2f);
        posCoda.x = Mathf.Clamp(canvasNuvola.transform.position.x, pannelloSfondo.position.x - limiteCoda, pannelloSfondo.position.x + limiteCoda);

        codaFumetto.position = posCoda;
    }

    private void OnTriggerEnter2D(Collider2D other) { if (other.CompareTag("Player")) giocatoreVicino = true; }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            giocatoreVicino = false;
            canvasNuvola.SetActive(false);
            indiceMessaggio = 0;
        }
    }
}