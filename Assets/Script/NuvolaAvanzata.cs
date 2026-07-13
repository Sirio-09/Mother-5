using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

// ============================================================================
// STRUTTURE DATI
// ============================================================================

[System.Serializable]
public class SegmentoDialogo
{
    public string speakerName;
    public AudioClip speakerVoice;
    [TextArea(3, 10)]
    public string[] dialogueLines;
}

[System.Serializable]
public class DialogueConfig
{
    [Header("Testo")]
    public float larghezzaMassima = 150f;
    public float altezzaMinima = 40f;
    public float paddingX = 20f;
    public float paddingY = 15f;

    [Header("Timing")]
    public float velocitaTesto = 0.03f;
    public float pausaVirgola = 0.15f;
    public float pausaPunto = 0.35f;
    public float limiteSovrapposizioneAudio = 0.04f;
    public float offsetCoda = 5f;

    [Header("Animazioni")]
    public AnimationCurve curvaPopIn = null;
    public float durataPopIn = 0.2f;
    public AnimationCurve curvaPopOut = null;
    public float durataPopOut = 0.2f;

    [Header("Effetto Onda")]
    public float velocitaShake = 8f;
    public float intensitaShake = 4f;

    [Header("Floating")]
    public float ampiezzaGalleggiamento = 2f;
    public float velocitaGalleggiamento = 4f;

    [Header("Input")]
    public float antiSpamCooldown = 0.2f;

    public void InitializeDefaults()
    {
        if (curvaPopIn == null || curvaPopIn.length == 0)
            curvaPopIn = AnimationCurve.EaseInOut(0, 0, 1, 1);
        if (curvaPopOut == null || curvaPopOut.length == 0)
            curvaPopOut = AnimationCurve.EaseInOut(0, 1, 1, 0);
    }
}

// ============================================================================
// INTERFACCE
// ============================================================================

public interface IInputHandler
{
    void Initialize();
    void OnInteractInput();
}

public interface IAudioPlayer
{
    void PlayVoiceClip(AudioClip clip, float volume = 0.5f);
}

// ============================================================================
// IMPLEMENTAZIONI INTERFACCE
// ============================================================================

public class LegacyInputHandler : MonoBehaviour, IInputHandler
{
    private NuvolaAvanzata dialogueSystem;

    public void Initialize()
    {
        dialogueSystem = GetComponent<NuvolaAvanzata>();
    }

    public void OnInteractInput()
    {
        if (dialogueSystem != null)
            dialogueSystem.HandleInteraction();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Z))
            OnInteractInput();
    }
}

public class NewInputSystemHandler : MonoBehaviour, IInputHandler
{
    private InputAction interactAction;
    private NuvolaAvanzata dialogueSystem;

    public void Initialize()
    {
        dialogueSystem = GetComponent<NuvolaAvanzata>();

        if (interactAction == null)
        {
            var inputMap = new InputActionMap("Dialogue");
            interactAction = inputMap.AddAction("Interact", InputActionType.Button);
            interactAction.AddBinding("<Keyboard>/z");
            inputMap.Enable();
            interactAction.performed += _ => OnInteractInput();
        }
    }

    public void OnInteractInput()
    {
        if (dialogueSystem != null)
            dialogueSystem.HandleInteraction();
    }

    void OnDestroy()
    {
        if (interactAction != null)
        {
            interactAction.performed -= _ => OnInteractInput();
            interactAction.Dispose();
        }
    }
}

public class AudioPlayerComponent : MonoBehaviour, IAudioPlayer
{
    private AudioSource audioSource;
    private float baseVolume = 0.5f;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.volume = baseVolume;
    }

    public void PlayVoiceClip(AudioClip clip, float volume = 0.5f)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.volume = volume;
            audioSource.PlayOneShot(clip);
        }
    }

    public void SetBaseVolume(float volume)
    {
        baseVolume = Mathf.Clamp01(volume);
        if (audioSource != null)
            audioSource.volume = baseVolume;
    }
}

// ============================================================================
// CLASSE PRINCIPALE
// ============================================================================

public class NuvolaAvanzata : MonoBehaviour
{
    [Header("━━━ RIFERIMENTI UI ━━━")]
    [SerializeField] private GameObject canvasNuvola;
    [SerializeField] private RectTransform pannelloSfondo;
    [SerializeField] private RectTransform codaFumetto;
    [SerializeField] private TextMeshProUGUI testoFumetto;

    [Header("━━━ DIALOGO E CONFIGURAZIONE ━━━")]
    [SerializeField] private List<SegmentoDialogo> dialogoCompleto = new List<SegmentoDialogo>();
    [SerializeField] private DialogueConfig config = new DialogueConfig();

    [Header("━━━ SISTEMA INPUT ━━━")]
    [SerializeField] private bool usaNewInputSystem = false;
    private IInputHandler inputHandler;

    [Header("━━━ BLOCCO MOVIMENTO PLAYER ━━━")]
    [Tooltip("Se attivo, blocca il movimento del Player all'inizio del dialogo.")]
    [SerializeField] private bool bloccaMovimentoPlayer = true;

    [Tooltip("OPZIONALE: trascina qui il GameObject del Player. Se vuoto, lo cercherà tramite il Tag 'Player'.")]
    [SerializeField] private GameObject playerCustomObject;

    [Tooltip("PROPRIETÀ INFALLIBILE: trascina qui i componenti/script del Player da disattivare durante il dialogo (es. il tuo script di movimento, PlayerController, ecc.).")]
    [SerializeField] private List<MonoBehaviour> componentiDaDisattivare = new List<MonoBehaviour>();

    [Tooltip("Se attivo, proverà a cercare e disattivare automaticamente componenti di movimento comuni sul player.")]
    [SerializeField] private bool disattivazioneAutomatica = true;

    [Header("━━━ AUDIO ━━━")]
    private IAudioPlayer audioPlayer;
    private float tempoUltimoAudio = 0f;

    [Header("━━━ UNITY EVENTS ━━━")]
    [SerializeField] private UnityEvent onDialogueStart = new UnityEvent();
    [SerializeField] private UnityEvent onDialogueEnd = new UnityEvent();
    [SerializeField] private UnityEvent<int> onSegmentChange = new UnityEvent<int>();
    [SerializeField] private UnityEvent<string> onSpeakerChange = new UnityEvent<string>();

    private bool giocatoreVicino = false;
    private Camera cam;
    private bool stoScrivendo = false;
    private bool inChiusura = false;
    private float antiSpamCooldown = 0f;
    private bool stoTremolando = false;

    private Coroutine coroutineScrittura;
    private Coroutine coroutineAnimazione;

    private int indiceSegmento = 0;
    private int indiceFrase = 0;

    private Vector3 scalaOriginaleCanvas;
    private CanvasGroup canvasGroup;

    public static bool skippaTutto = false;
    public static bool dialogoInCorso = false;

    void Awake()
    {
        InitializeComponents();
        InitializeInput();
        InitializeConfig();
    }

    private void InitializeComponents()
    {
        cam = Camera.main;

        if (canvasNuvola != null)
        {
            canvasGroup = canvasNuvola.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = canvasNuvola.AddComponent<CanvasGroup>();
            }
            canvasGroup.alpha = 0f;

            scalaOriginaleCanvas = canvasNuvola.transform.localScale;
            // Base invisibile, scala leggermente ridotta pronta per l'apertura
            canvasNuvola.transform.localScale = scalaOriginaleCanvas * 0.8f;
            canvasNuvola.SetActive(false);
        }
        else
        {
            Debug.LogError("[NuvolaAvanzata] Manca il riferimento a 'Canvas Nuvola' nell'Inspector!");
        }

        var audioComp = gameObject.GetComponent<AudioPlayerComponent>();
        if (audioComp == null)
            audioComp = gameObject.AddComponent<AudioPlayerComponent>();
        audioPlayer = audioComp;
    }

    private void InitializeInput()
    {
        if (usaNewInputSystem)
        {
            var newInputHandler = gameObject.AddComponent<NewInputSystemHandler>();
            newInputHandler.Initialize();
            inputHandler = newInputHandler;
        }
        else
        {
            var legacyHandler = gameObject.AddComponent<LegacyInputHandler>();
            legacyHandler.Initialize();
            inputHandler = legacyHandler;
        }
    }

    private void InitializeConfig()
    {
        config.InitializeDefaults();
    }

    void Update()
    {
        UpdateAntiSpamCooldown();
        UpdateTextAnimations();
    }

    private void UpdateAntiSpamCooldown()
    {
        if (antiSpamCooldown > 0f)
            antiSpamCooldown -= Time.unscaledDeltaTime;
    }

    private void UpdateTextAnimations()
    {
        if (canvasNuvola != null && canvasNuvola.activeSelf)
        {
            // FIX DEFINITIVO: Continua SEMPRE a ricalcolare la posizione, anche mentre si chiude!
            // Altrimenti la coda del fumetto si scollega dalla nuvola in rimpicciolimento causando il glitch visivo.
            MantieniPannelloNelloSchermo();

            // Il tremolio del testo invece si può tranquillamente fermare durante la chiusura
            if (!inChiusura)
            {
                AnimaVerticiTesto();
            }
        }
    }

    // ========================================================================
    // GESTIONE BLOCCO PLAYER
    // ========================================================================

    private void GestisciBloccoPlayer(bool blocca)
    {
        if (!bloccaMovimentoPlayer) return;

        GameObject player = playerCustomObject;
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
        }

        if (player == null) return;

        if (componentiDaDisattivare != null)
        {
            foreach (var comp in componentiDaDisattivare)
            {
                if (comp != null) comp.enabled = !blocca;
            }
        }

        if (disattivazioneAutomatica)
        {
            if (player.TryGetComponent<Rigidbody2D>(out var rb2d))
            {
                if (blocca) rb2d.linearVelocity = Vector2.zero;
            }
            if (player.TryGetComponent<Rigidbody>(out var rb3d))
            {
                if (blocca) rb3d.linearVelocity = Vector3.zero;
            }

            if (player.TryGetComponent<PlayerInput>(out var playerInput))
            {
                playerInput.enabled = !blocca;
            }

            MonoBehaviour[] tuttiIComponenti = player.GetComponentsInChildren<MonoBehaviour>();
            foreach (var comp in tuttiIComponenti)
            {
                if (comp == null || comp == this) continue;
                if (componentiDaDisattivare.Contains(comp)) continue;

                string nomeTipo = comp.GetType().Name.ToLower();

                if (nomeTipo.Contains("move") ||
                    nomeTipo.Contains("control") ||
                    nomeTipo.Contains("input") ||
                    nomeTipo.Contains("walk") ||
                    nomeTipo.Contains("player") ||
                    nomeTipo.Contains("physics") ||
                    nomeTipo.Contains("motor"))
                {
                    comp.enabled = !blocca;
                }
            }

            if (player.TryGetComponent<Animator>(out var anim))
            {
                if (blocca)
                {
                    foreach (var param in anim.parameters)
                    {
                        if (param.type == AnimatorControllerParameterType.Float)
                        {
                            if (param.name.ToLower().Contains("speed") ||
                                param.name.ToLower().Contains("velocity") ||
                                param.name.ToLower().Contains("move"))
                            {
                                anim.SetFloat(param.name, 0f);
                            }
                        }
                        else if (param.type == AnimatorControllerParameterType.Bool)
                        {
                            if (param.name.ToLower().Contains("walking") ||
                                param.name.ToLower().Contains("moving") ||
                                param.name.ToLower().Contains("running"))
                            {
                                anim.SetBool(param.name, false);
                            }
                        }
                    }
                }
            }
        }
    }

    // ========================================================================
    // GESTIONE DIALOGO
    // ========================================================================

    public void HandleInteraction()
    {
        if (dialogoCompleto == null || dialogoCompleto.Count == 0) return;
        if (antiSpamCooldown > 0f) return;
        if (inChiusura) return;

        antiSpamCooldown = config.antiSpamCooldown;

        if (!canvasNuvola.activeSelf)
        {
            if (!giocatoreVicino) return;

            dialogoInCorso = true;
            skippaTutto = false;
            indiceSegmento = 0;
            indiceFrase = 0;

            GestisciBloccoPlayer(true);
            ApriFumetto();
        }
        else
        {
            if (stoScrivendo)
                CompletaTesto();
            else
                AvanzaDialogo();
        }
    }

    private void AvanzaDialogo()
    {
        bool hasMoreLines = dialogoCompleto[indiceSegmento].dialogueLines != null &&
                           indiceFrase < dialogoCompleto[indiceSegmento].dialogueLines.Length - 1;

        if (hasMoreLines)
        {
            indiceFrase++;
            ApriFumetto();
        }
        else if (indiceSegmento < dialogoCompleto.Count - 1)
        {
            indiceSegmento++;
            indiceFrase = 0;
            onSegmentChange.Invoke(indiceSegmento);
            ApriFumetto();
        }
        else
        {
            ChiudiFumetto();
        }
    }

    // ========================================================================
    // APERTURA / CHIUSURA 
    // ========================================================================

    void ApriFumetto()
    {
        if (canvasNuvola == null || testoFumetto == null) return;

        ClearTextMeshProData();

        if (!canvasNuvola.activeSelf)
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            canvasNuvola.transform.localScale = scalaOriginaleCanvas * 0.8f;
        }

        canvasNuvola.SetActive(true);
        MantieniPannelloNelloSchermo();

        if (coroutineScrittura != null) StopCoroutine(coroutineScrittura);
        coroutineScrittura = StartCoroutine(EffettoMacchinaDaScrivere());

        if (indiceSegmento == 0 && indiceFrase == 0)
        {
            if (coroutineAnimazione != null) StopCoroutine(coroutineAnimazione);
            coroutineAnimazione = StartCoroutine(AnimazionePopIn());
            onDialogueStart.Invoke();
        }

        onSpeakerChange.Invoke(dialogoCompleto[indiceSegmento].speakerName);
    }

    void ChiudiFumetto()
    {
        inChiusura = true;
        stoTremolando = false;

        if (canvasNuvola == null || !gameObject.activeInHierarchy)
        {
            ResettaTutto();
            return;
        }

        if (coroutineScrittura != null) StopCoroutine(coroutineScrittura);
        if (coroutineAnimazione != null) StopCoroutine(coroutineAnimazione);

        coroutineAnimazione = StartCoroutine(AnimazionePopOut());
    }

    void ResettaTutto()
    {
        ClearTextMeshProData();

        if (canvasNuvola != null)
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            canvasNuvola.transform.localScale = scalaOriginaleCanvas * 0.8f;
            canvasNuvola.SetActive(false);
        }

        stoTremolando = false;
        dialogoInCorso = false;
        inChiusura = false;
        antiSpamCooldown = config.antiSpamCooldown;

        GestisciBloccoPlayer(false);
        onDialogueEnd.Invoke();
    }

    private void ClearTextMeshProData()
    {
        if (testoFumetto == null) return;
        testoFumetto.text = string.Empty;
        testoFumetto.maxVisibleCharacters = 0;
    }

    // ========================================================================
    // ANIMAZIONI E MACCHINA DA SCRIVERE
    // ========================================================================

    IEnumerator AnimazionePopIn()
    {
        float tempoTrascorso = 0f;

        // Partiamo dall'80% in modo coerente
        Vector3 scalaPartenza = scalaOriginaleCanvas * 0.8f;

        while (tempoTrascorso < config.durataPopIn)
        {
            tempoTrascorso += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(config.curvaPopIn.Evaluate(tempoTrascorso / config.durataPopIn));
            float fadeT = Mathf.Clamp01(tempoTrascorso / config.durataPopIn); // Fade lineare liscio

            canvasNuvola.transform.localScale = Vector3.Lerp(scalaPartenza, scalaOriginaleCanvas, t);

            if (canvasGroup != null) canvasGroup.alpha = fadeT;

            yield return null;
        }

        canvasNuvola.transform.localScale = scalaOriginaleCanvas;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    IEnumerator AnimazionePopOut()
    {
        float tempoTrascorso = 0f;
        Vector3 scalaAttuale = canvasNuvola.transform.localScale;

        // LA VERA SICUREZZA: non scaliamo mai a zero, così non spacchiamo la matematica della UI.
        // Ridurlo all'80% è sufficiente per l'effetto visivo, il resto lo fa la trasparenza.
        Vector3 scalaFinale = scalaOriginaleCanvas * 0.8f;

        while (tempoTrascorso < config.durataPopOut)
        {
            tempoTrascorso += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(config.curvaPopOut.Evaluate(tempoTrascorso / config.durataPopOut));
            float fadeT = Mathf.Clamp01(tempoTrascorso / config.durataPopOut);

            canvasNuvola.transform.localScale = Vector3.Lerp(scalaAttuale, scalaFinale, t);

            if (canvasGroup != null) canvasGroup.alpha = 1f - fadeT;

            yield return null;
        }

        if (canvasGroup != null) canvasGroup.alpha = 0f;

        // Aspettiamo che il frame sia interamente finito per evitare ricalcoli postumi
        yield return new WaitForEndOfFrame();

        canvasNuvola.transform.localScale = scalaOriginaleCanvas * 0.8f;
        ClearTextMeshProData();
        canvasNuvola.SetActive(false);

        stoTremolando = false;
        dialogoInCorso = false;
        inChiusura = false;
        antiSpamCooldown = config.antiSpamCooldown;

        GestisciBloccoPlayer(false);
        onDialogueEnd.Invoke();
    }

    IEnumerator EffettoMacchinaDaScrivere()
    {
        stoScrivendo = true;

        if (dialogoCompleto[indiceSegmento].dialogueLines == null ||
            dialogoCompleto[indiceSegmento].dialogueLines.Length == 0)
        {
            stoScrivendo = false;
            yield break;
        }

        string fraseCorrente = dialogoCompleto[indiceSegmento].dialogueLines[indiceFrase];

        if (fraseCorrente.Contains("~"))
        {
            fraseCorrente = fraseCorrente.Replace("~", "");
            stoTremolando = true;
        }
        else
        {
            stoTremolando = false;
        }

        testoFumetto.text = fraseCorrente;
        testoFumetto.maxVisibleCharacters = 0;
        testoFumetto.rectTransform.sizeDelta = new Vector2(config.larghezzaMassima, 1000f);
        testoFumetto.ForceMeshUpdate();

        Vector2 dimensions = testoFumetto.GetRenderedValues(false);
        float w = Mathf.Min(dimensions.x, config.larghezzaMassima);
        float h = Mathf.Max(dimensions.y, config.altezzaMinima);
        testoFumetto.rectTransform.sizeDelta = new Vector2(w, h);

        if (pannelloSfondo != null)
            pannelloSfondo.sizeDelta = new Vector2(w + config.paddingX, h + config.paddingY);

        Canvas.ForceUpdateCanvases();
        MantieniPannelloNelloSchermo();

        int totaleCaratteri = testoFumetto.textInfo.characterCount;
        AudioClip voceCorrente = dialogoCompleto[indiceSegmento].speakerVoice;

        for (int i = 0; i < totaleCaratteri; i++)
        {
            testoFumetto.maxVisibleCharacters = i + 1;

            if (i >= testoFumetto.textInfo.characterInfo.Length) break;

            char c = testoFumetto.textInfo.characterInfo[i].character;
            PlayVoiceForCharacter(voceCorrente, c);

            float delayToContinue = GetDelayForCharacter(c);
            yield return new WaitForSeconds(delayToContinue);
        }

        stoScrivendo = false;
    }

    private void PlayVoiceForCharacter(AudioClip voiceClip, char character)
    {
        if (voiceClip != null && audioPlayer != null && character != ' ')
        {
            if (Time.unscaledTime - tempoUltimoAudio > config.limiteSovrapposizioneAudio)
            {
                audioPlayer.PlayVoiceClip(voiceClip, 0.5f);
                tempoUltimoAudio = Time.unscaledTime;
            }
        }
    }

    private float GetDelayForCharacter(char character)
    {
        return character switch
        {
            '.' or '!' or '?' => config.pausaPunto,
            ',' or ':' or ';' => config.pausaVirgola,
            _ => config.velocitaTesto
        };
    }

    void CompletaTesto()
    {
        if (coroutineScrittura != null) StopCoroutine(coroutineScrittura);

        if (testoFumetto != null)
        {
            testoFumetto.maxVisibleCharacters = testoFumetto.textInfo.characterCount;
            testoFumetto.ForceMeshUpdate();
        }

        stoScrivendo = false;
    }

    // ========================================================================
    // EFFETTI DI RENDERING E POSIZIONAMENTO
    // ========================================================================

    void AnimaVerticiTesto()
    {
        if (!stoTremolando || testoFumetto == null) return;

        testoFumetto.ForceMeshUpdate();
        TMP_TextInfo textInfo = testoFumetto.textInfo;

        if (textInfo == null || textInfo.meshInfo == null ||
            textInfo.meshInfo.Length == 0 || textInfo.characterCount == 0) return;

        int characterCount = textInfo.characterCount;
        float tempoSicuro = Mathf.Repeat(Time.unscaledTime, 1000f);

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];

            if (!charInfo.isVisible || i >= testoFumetto.maxVisibleCharacters) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;

            if (materialIndex < 0 || materialIndex >= textInfo.meshInfo.Length) continue;

            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

            if (vertices == null || vertexIndex + 3 >= vertices.Length) continue;

            float offset = Mathf.Sin(tempoSicuro * config.velocitaShake + i * 0.5f) * config.intensitaShake;

            vertices[vertexIndex + 0].y += offset;
            vertices[vertexIndex + 1].y += offset;
            vertices[vertexIndex + 2].y += offset;
            vertices[vertexIndex + 3].y += offset;
        }

        testoFumetto.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    void MantieniPannelloNelloSchermo()
    {
        if (cam == null || pannelloSfondo == null || codaFumetto == null) return;

        float metaAltezzaCam = cam.orthographicSize;
        float metaLarghezzaCam = cam.aspect * metaAltezzaCam;
        float margine = 0.2f;

        float offsetFluttuante = Mathf.Sin(
            Mathf.Repeat(Time.unscaledTime, 1000f) * config.velocitaGalleggiamento
        ) * config.ampiezzaGalleggiamento;

        Vector3 posPannello = canvasNuvola.transform.position;
        float altezzaPannelloMondo = pannelloSfondo.sizeDelta.y * canvasNuvola.transform.localScale.y;
        float metaLarghezzaPannelloMondo = (pannelloSfondo.sizeDelta.x * canvasNuvola.transform.localScale.x) / 2f;

        posPannello.y += (altezzaPannelloMondo / 2f) + (offsetFluttuante * canvasNuvola.transform.localScale.y);

        posPannello.x = Mathf.Clamp(
            posPannello.x,
            (cam.transform.position.x - metaLarghezzaCam + margine) + metaLarghezzaPannelloMondo,
            (cam.transform.position.x + metaLarghezzaCam - margine) - metaLarghezzaPannelloMondo
        );

        pannelloSfondo.position = posPannello;

        Vector3 posCoda = codaFumetto.position;
        posCoda.y = pannelloSfondo.position.y - (altezzaPannelloMondo / 2f) +
                    (config.offsetCoda * canvasNuvola.transform.localScale.y);

        float limiteCoda = metaLarghezzaPannelloMondo -
                          ((codaFumetto.sizeDelta.x * canvasNuvola.transform.localScale.x) / 2f);
        posCoda.x = Mathf.Clamp(
            canvasNuvola.transform.position.x,
            pannelloSfondo.position.x - limiteCoda,
            pannelloSfondo.position.x + limiteCoda
        );

        codaFumetto.position = posCoda;

        if (pannelloSfondo.sizeDelta.x < codaFumetto.sizeDelta.x + 5f)
            codaFumetto.gameObject.SetActive(false);
        else
            codaFumetto.gameObject.SetActive(true);
    }

    // ========================================================================
    // COLLIDER TRIGGER
    // ========================================================================

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            giocatoreVicino = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            giocatoreVicino = false;

            if (canvasNuvola != null && canvasNuvola.activeSelf && !inChiusura)
                ChiudiFumetto();
        }
    }

    private void OnDisable()
    {
        ResettaTutto();
    }

    // ========================================================================
    // API ESTERNA (METODI PUBBLICI)
    // ========================================================================

    public void StartDialogueAtSegment(int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= dialogoCompleto.Count) return;

        dialogoInCorso = true;
        skippaTutto = false;
        indiceSegmento = segmentIndex;
        indiceFrase = 0;

        GestisciBloccoPlayer(true);
        ApriFumetto();
    }

    public void SkipAllDialogue()
    {
        skippaTutto = true;
        ChiudiFumetto();
    }

    public int GetTotalSegments() { return dialogoCompleto.Count; }
    public int GetCurrentSegment() { return indiceSegmento; }
    public bool IsDialogueActive() { return dialogoInCorso && canvasNuvola.activeSelf; }

    public void SetDialogueConfig(DialogueConfig newConfig)
    {
        config = newConfig;
        config.InitializeDefaults();
    }

    public bool IsPlayerNearby => giocatoreVicino;
    public bool IsWriting => stoScrivendo;
    public bool IsClosing => inChiusura;
    public int CurrentDialogueIndex => indiceSegmento;
    public int CurrentLineIndex => indiceFrase;
}