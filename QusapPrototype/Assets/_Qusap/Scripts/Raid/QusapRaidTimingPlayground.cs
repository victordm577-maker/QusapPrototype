using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // Diagnostic preparation only; time, expiry, loot and Winner belong to the real session.
    public sealed class QusapRaidTimingPlayground : MonoBehaviour
    {
        [SerializeField] private GameObject roundTemplate;
        [SerializeField] private QusapRaidLootCatalog catalog;
        [SerializeField] private bool reducedDiagnosticTimes = true;
        [SerializeField] private float diagnosticOpening = 2;
        [SerializeField] private float diagnosticDurationA = 15;
        [SerializeField] private float diagnosticDurationB = 6;
        private GameObject round;
        private Text title, diagnostics, summary;
        private Button start;
        public QusapSharedRaidContext Context { get; private set; }
        public QusapPersistentStashRepository[] Profiles { get; private set; }
        public QusapRaidBootstrap Raid { get; private set; }
        public Canvas Canvas { get; private set; }
        public int RaidNumber { get; private set; }
        public string LastSummary { get; private set; } = "A: permanece en la salida; espera apertura y cinco segundos. B: dos Active esperan la expiracion.";
        public bool ReducedDiagnosticTimes => reducedDiagnosticTimes;
        public void Configure(GameObject template, QusapRaidLootCatalog definitions) { roundTemplate = template; catalog = definitions; }
        public void ConfigureTimes(QusapRaidTimingSettings settings)
        {
            if (Context != null) throw new InvalidOperationException("Configure before deployment");
            reducedDiagnosticTimes = settings.DurationSeconds != 480 || settings.ExtractionOpeningSeconds != 180;
            diagnosticDurationA = diagnosticDurationB = (float)settings.DurationSeconds;
            diagnosticOpening = (float)settings.ExtractionOpeningSeconds;
        }
        private void Start() { BuildHud(); Refresh(); }
        public bool BeginRaid()
        {
            if (Context != null && !Context.Session.IsFinished) return false;
            Context?.Dispose();
            if (round != null) { round.SetActive(false); Destroy(round); }
            var settings = reducedDiagnosticTimes ? new QusapRaidTimingSettings(RaidNumber == 0 ? diagnosticDurationA : diagnosticDurationB, diagnosticOpening)
                : new QusapRaidTimingSettings();
            // Fresh isolated profiles for each case: B proves that expiry grants no new horn.
            Profiles = Enumerable.Range(0,2).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(),"QusapTimingMemoryOnly",Guid.NewGuid().ToString("N")),catalog.Definitions,
                "timing/P"+(i+1)+"/"+Guid.NewGuid().ToString("N"),new QusapMemoryProfileFiles())).ToArray();
            round = Instantiate(roundTemplate); round.name = "TimingRound_"+(RaidNumber+1);
            var actors = round.GetComponentInChildren<QusapRaidBootstrap>(true).Participants;
            var positions = RaidNumber == 0 ? new[] {new Vector3(4.4f,1.05f,0),new Vector3(-.85f,1.05f,0)}
                : new[] {new Vector3(-.85f,1.05f,0),new Vector3(.85f,1.05f,0)};
            for(int i=0;i<2;i++) actors[i].transform.position=positions[i];
            round.GetComponentInChildren<QusapCombatArenaController>(true).Configure(actors[0].GetComponent<QusapInputReader>(),
                actors[1].GetComponent<QusapInputReader>(),positions[0],positions[1],true);
            round.SetActive(true); Raid=round.GetComponentInChildren<QusapRaidBootstrap>();
            var native=round.GetComponentInChildren<QusapWeaponMatchBootstrap>(); native.TryInitialize();
            var context=new QusapSharedRaidContext(settings,"P1","P2");
            for(int i=0;i<2;i++)
            {
                var actor=Raid.Participants[i]; actor.Configure(actor.ParticipantId,Profiles[i].ProfileId);
                Profiles[i].ObserveNativeAllocator(()=>native.NextWeaponInstanceId);
                var deployment=new QusapRaidDeploymentAuthority(Profiles[i],catalog,actor.ParticipantId,
                    actor.GetComponent<QusapWeaponEquipment>(),d=>native.CreateRaidLootWeapon(d.WeaponDefinitionId));
                if(!deployment.PrepareLoadout()||!context.Add(deployment)) throw new InvalidOperationException("Timing loadout rejected");
            }
            if(!context.CommitDeployment()||!context.StartRunning()) throw new InvalidOperationException(context.LastResult);
            Context=context; Raid.BeginSharedDeploymentSession(context); RaidNumber++;
            LastSummary="CASO "+(RaidNumber==1?"A":"B")+" EN CURSO · perfiles nuevos · sin concesiones preparadas.";
            for(int i=0;i<2;i++)
            {
                Give(i,"raid_fragment");
                var pocket=Give(i,"raid_rare_relic");
                if(Raid.Participants[i].TryMoveToSecurePocket(pocket.LootInstanceId)!=QusapLootResult.Success) throw new InvalidOperationException("Pocket rejected");
            }
            Context.Session.Competition.SessionFinished += result =>
            {
                LastSummary="CASO "+(RaidNumber==1?"A":"B")+" · Motivo: "+result.Reason+" · Ganador: "+(result.WinnerId??"Sin ganador")+"\n"
                    +string.Join(" · ",result.Participants.Select(p=>p.ParticipantId+": "+p.Outcome))+"\n"
                    +string.Join(" · ",Profiles.Select((p,i)=>"P"+(i+1)+" stash "+p.ReadStashSnapshot(p.ProfileId).Count
                        +" · Cuerno "+(p.HasTriumphHorn?"desbloqueado":"bloqueado")+" · concesiones "+p.TriumphHornGrantsPerformed));
                start.gameObject.SetActive(true); start.GetComponentInChildren<Text>().text="Nueva raid · caso B · perfiles nuevos";
                Debug.Log("RAID_TIMING_FINISHED "+Context.RaidId+" "+LastSummary);
            };
            start.gameObject.SetActive(false); Refresh(); return true;
        }
        private QusapLootInstance Give(int index,string id)
        {
            if(!catalog.TryResolve(id,out var definition)) throw new InvalidOperationException(id);
            var item=Context.Session.World.Create(definition,"Timing diagnostic pickup");
            if(Raid.Participants[index].State.TryPickup(item.LootInstanceId)!=QusapLootResult.Success) throw new InvalidOperationException("Pickup rejected");
            return item;
        }
        private void Update() { Refresh(); }
        private void Refresh()
        {
            if(title==null) return;
            title.text="RAID TIMING · APERTURA DE EXTRACCIONES\n"+(reducedDiagnosticTimes?"TIEMPOS REDUCIDOS SOLO PARA DIAGNOSTICO":"TIEMPOS NORMALES")
                +" · Valores normales: 480 / 180 / 5 segundos";
            summary.text=LastSummary;
            if(Context==null) {diagnostics.text="Deployment pendiente · reloj detenido · sin perfiles reales. P1: teclado A/D · P2: gamepad.";return;}
            var session=Context.Session;var clock=session.Timing;
            diagnostics.text="Caso "+(RaidNumber==1?"A":"B")+" · Duracion "+clock.Settings.DurationSeconds+" s · Apertura "+clock.Settings.ExtractionOpeningSeconds+" s\n"
                +"Transcurrido "+clock.ElapsedSeconds.ToString("F2")+" s · Restante "+clock.RemainingSeconds.ToString("F2")+" s · Hasta apertura "+clock.SecondsUntilExtraction.ToString("F2")+" s\n"
                +"Extracciones: "+(session.CanExtract?"ABIERTAS":clock.HasExpired?"CERRADAS POR EXPIRACION":"BLOQUEADAS")+" · Roster cerrado: "+session.Competition.RosterClosed+" · "+session.Competition.Status+"\n"
                +string.Join(" · ",Raid.Participants.Select(p=>p.ParticipantId+": "+Outcome(p)+" · "+ExtractionProgress(p)))+"\n"
                +"Active: "+session.Competition.ActiveCount+" · Ganador: "+(session.Competition.WinnerId??"Sin ganador")+" · Prestamos destruidos: "+session.World.LoanersDestroyed+"\n"
                +string.Join(" · ",Profiles.Select((profile,i)=>"P"+(i+1)+" stash "+profile.ReadStashSnapshot(profile.ProfileId).Count
                    +" · Cuerno "+(profile.HasTriumphHorn?"desbloqueado":"bloqueado")+" · concesiones "+profile.TriumphHornGrantsPerformed))+"\n"
                +"RaidId: "+Context.RaidId;
        }
        private string Outcome(QusapRaidInventory p) => Context.Session.Competition.TryGetOutcome(p.ParticipantId,out var state)?state.ToString():"Deployment";
        private static string ExtractionProgress(QusapRaidInventory participant)
        {
            // Winner shares the retirement signal with extraction; its completed timer is not physical progress.
            if(participant.State.Status==QusapRaidInventoryStatus.Winner) return "No aplica · Winner";
            var countdown=participant.GetComponent<QusapRaidExtraction>().Countdown;
            return "cuenta "+countdown.Elapsed.ToString("F2")+"/"+countdown.Duration.ToString("0.##")+" s";
        }
        private void BuildHud()
        {
            var root=new GameObject("TimingCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            root.transform.SetParent(transform,false);Canvas=root.GetComponent<Canvas>();Canvas.renderMode=RenderMode.ScreenSpaceOverlay;Canvas.sortingOrder=20;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new(1280,720);
            title=Label(root.transform,"TimingTitle",25,12,1230,70,22);
            diagnostics=Label(root.transform,"TimingDiagnostics",25,88,1230,190,18);
            summary=Label(root.transform,"TimingSummary",25,585,1230,120,18);
            var button=new GameObject("IniciarRaid",typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(root.transform,false);
            var rect=button.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new(.5f,0);rect.pivot=new(.5f,0);rect.anchoredPosition=new(0,145);rect.sizeDelta=new(600,42);
            button.GetComponent<Image>().color=new(.12f,.32f,.24f);start=button.GetComponent<Button>();start.onClick.AddListener(()=>BeginRaid());
            Label(button.transform,"BeginLabel",0,0,600,42,20).text="Iniciar caso A · despliegue real";
        }
        private static Text Label(Transform parent,string name,float x,float y,float w,float h,int size)
        {
            var root=new GameObject(name,typeof(RectTransform),typeof(Image));root.transform.SetParent(parent,false);
            var rect=root.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new(0,1);rect.pivot=new(0,1);rect.anchoredPosition=new(x,-y);rect.sizeDelta=new(w,h);
            root.GetComponent<Image>().color=new(.025f,.04f,.065f,.9f);
            var go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(root.transform,false);
            var text=go.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=size;text.color=Color.white;text.raycastTarget=false;
            text.rectTransform.anchorMin=Vector2.zero;text.rectTransform.anchorMax=Vector2.one;text.rectTransform.offsetMin=new(10,3);text.rectTransform.offsetMax=new(-10,-3);return text;
        }
        private void OnDestroy() {Context?.Dispose();}
    }
}
