using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // Diagnostic preparation only; time, expiry, loot and Winner belong to the real session.
    public sealed class QusapRaidHazardPlayground : MonoBehaviour
    {
        [SerializeField] private GameObject roundTemplate;
        [SerializeField] private QusapRaidLootCatalog catalog;
        [SerializeField] private bool reducedDiagnosticTimes = true;
        [SerializeField] private float diagnosticOpening = 2;
        [SerializeField] private float diagnosticDurationA = 36;
        [SerializeField] private float diagnosticDurationB = 36;
        private GameObject round;
        private Text title, diagnostics, summary;
        private Button start;
        [Serializable] public sealed class StageData
        {
            public double startsAt; public Vector4 bounds; public float damage;
            public StageData(double time,Vector4 area,float amount) { startsAt=time;bounds=area;damage=amount; }
            public QusapRaidHazardStage Build(double warning) => new(startsAt,new(bounds.x,bounds.y,bounds.z,bounds.w),damage,warning);
        }
        [SerializeField] private Vector4 initialBounds = new(-12,-.5f,12,11);
        [SerializeField] private float tickSeconds = 1;
        [SerializeField] private float normalWarningSeconds = 15, diagnosticWarningSeconds = 3;
        [SerializeField] private StageData[] normalStages = {
            new(240,new(-10,-.5f,10,10),10),new(300,new(-7,-.5f,7,9),12),
            new(360,new(-4,-.5f,4,8),15),new(420,new(-2.5f,-.5f,2.5f,6),20)};
        [SerializeField] private StageData[] diagnosticStages = {
            new(6,new(-10,-.5f,10,10),10),new(12,new(-7,-.5f,7,9),12),
            new(18,new(-4,-.5f,4,8),15),new(24,new(-2.5f,-.5f,2.5f,6),20)};
        private QusapRaidHazardSettings overrideHazard;
        private LineRenderer currentOutline,nextOutline;
        private readonly Transform[] dangerPanels = new Transform[4];
        private Material boundaryMaterial,warningMaterial,dangerMaterial;
        public QusapRaidHazard Hazard { get; private set; }
        public QusapRaidHazardSettings ReadHazardSettings() => overrideHazard ?? new(
            new(initialBounds.x,initialBounds.y,initialBounds.z,initialBounds.w),
            (reducedDiagnosticTimes ? diagnosticStages : normalStages).Select(s=>s.Build(reducedDiagnosticTimes ? diagnosticWarningSeconds : normalWarningSeconds)),tickSeconds);
        public QusapSharedRaidContext Context { get; private set; }
        public QusapPersistentStashRepository[] Profiles { get; private set; }
        public QusapRaidBootstrap Raid { get; private set; }
        public Canvas Canvas { get; private set; }
        public int RaidNumber { get; private set; }
        public string LastSummary { get; private set; } = "P1: A hacia el centro para salir del peligro; P2: gamepad. Salud normal, daño ambiental real.";
        public bool ReducedDiagnosticTimes => reducedDiagnosticTimes;
        public void Configure(GameObject template, QusapRaidLootCatalog definitions) { roundTemplate = template; catalog = definitions; }
        public void ConfigureTimes(QusapRaidTimingSettings settings,QusapRaidHazardSettings hazardSettings = null)
        {
            if (Context != null) throw new InvalidOperationException("Configure before deployment");
            overrideHazard=hazardSettings;
            reducedDiagnosticTimes = settings.DurationSeconds != 480 || settings.ExtractionOpeningSeconds != 180;
            diagnosticDurationA = diagnosticDurationB = (float)settings.DurationSeconds;
            diagnosticOpening = (float)settings.ExtractionOpeningSeconds;
        }
        private void Start() { BuildHud(); BuildBoundaries(); Refresh(); }
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
            var positions = new[] {new Vector3(10.8f,1.05f,0),new Vector3(-10.8f,1.05f,0)};
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
            var driver=Raid.GetComponent<QusapRaidHazardDriver>(); driver.Bind(ReadHazardSettings()); Hazard=driver.Hazard;
            LastSummary="Incursión "+RaidNumber+" · 100 HP normales · mantén P1 fuera para ver daño; entra al centro para detenerlo.";
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
                start.gameObject.SetActive(true); start.GetComponentInChildren<Text>().text="Nueva incursión · perfiles nuevos";
                Debug.Log("RAID_HAZARD_FINISHED "+Context.RaidId+" "+LastSummary);
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
            title.text="PELIGRO PROGRESIVO · RECTÁNGULOS X/Y\n"+(reducedDiagnosticTimes ? "DIAGNÓSTICO: 6 / 12 / 18 / 24 s · plazo 36 · apertura 2" : "NORMAL: 240 / 300 / 360 / 420 s · plazo 480 · apertura 180");
            summary.text=LastSummary;
            if(Context==null) { diagnostics.text="Deployment pendiente · sin reloj ni peligro. P1 A/D · salto y dash existentes. P2 gamepad."; return; }
            var session=Context.Session;var clock=session.Timing;var next=Hazard.NextStage;
            diagnostics.text="Tiempo "+clock.ElapsedSeconds.ToString("F2")+" / "+clock.Settings.DurationSeconds+" s · etapa "+(Hazard.StageIndex+1)
                +" · próximo cambio "+(next==null ? "zona final" : (next.StartsAt-clock.ElapsedSeconds).ToString("F2")+" s")+"\n"
                +"Área actual: "+Hazard.CurrentArea+"\nÁrea siguiente: "+(next==null ? "sin más cierres" : next.Area.ToString())+"\n"
                +(Hazard.WarningActive ? "AVISO: el límite amarillo se cierra próximamente" : Hazard.StageIndex<0 ? "SIN PELIGRO TODAVÍA" : "PELIGRO ROJO · seguro dentro del borde verde")+"\n"
                +string.Join(" · ",Raid.Participants.Select(p=>p.ParticipantId+": "+Outcome(p)+" · "+(Hazard.IsExposed(p)?"EXPUESTO":"seguro / terminal")
                    +" · HP "+p.Receiver.CurrentHealth.ToString("F0")+"/"+p.Receiver.MaxHealth+" · daño peligro "+Hazard.DamageAppliedTo(p.ParticipantId).ToString("F0")))+"\n"
                +"Extracción CYAN: "+(session.CanExtract?"abierta":"cerrada")+" · "+string.Join(" · ",Raid.Participants.Select(p=>p.ParticipantId+" "+ExtractionProgress(p)))+"\n"
                +"Daño "+(Hazard.StageIndex<0 ? "0" : Hazard.Settings.Stages[Hazard.StageIndex].Damage.ToString())+" cada "+Hazard.Settings.TickSeconds
                +" s · ticks "+Hazard.TicksProcessed+" · pendientes "+session.Competition.EnvironmentalDeathsPending+" · ganador "+(session.Competition.WinnerId??"ninguno");
            UpdateBoundaries();
        }
        private void BuildBoundaries()
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            boundaryMaterial=new Material(shader){color=new Color(.2f,1,.35f)};
            warningMaterial=new Material(shader){color=Color.yellow};dangerMaterial=new Material(shader){color=new Color(.23f,.055f,.04f)};
            currentOutline=Outline("SafeArea_Green",boundaryMaterial);nextOutline=Outline("NextArea_WarningYellow",warningMaterial);
            for(int i=0;i<4;i++)
            {
                var go=new GameObject("Hazard_Background_"+i,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
                var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f),new Vector3(.5f,-.5f)};
                mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();go.GetComponent<MeshFilter>().mesh=mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial=dangerMaterial;dangerPanels[i]=go.transform;go.SetActive(false);
            }
            nextOutline.gameObject.SetActive(false);currentOutline.gameObject.SetActive(false);
        }
        private LineRenderer Outline(string name,Material material)
        {
            var go=new GameObject(name,typeof(LineRenderer));go.transform.SetParent(transform,false);
            var line=go.GetComponent<LineRenderer>();line.sharedMaterial=material;line.loop=true;line.positionCount=4;line.widthMultiplier=.06f;return line;
        }
        private static void Draw(LineRenderer line,QusapRaidSafeArea area)
            => line.SetPositions(new[]{new Vector3(area.MinX,area.MinY,-.6f),new Vector3(area.MinX,area.MaxY,-.6f),new Vector3(area.MaxX,area.MaxY,-.6f),new Vector3(area.MaxX,area.MinY,-.6f)});
        private void UpdateBoundaries()
        {
            currentOutline.gameObject.SetActive(true);Draw(currentOutline,Hazard.CurrentArea);
            nextOutline.gameObject.SetActive(Hazard.WarningActive);if(Hazard.NextStage!=null)Draw(nextOutline,Hazard.NextStage.Area);
            var a=Hazard.CurrentArea;
            Panel(0,-14,-1,a.MinX,12);Panel(1,a.MaxX,-1,14,12);Panel(2,a.MinX,-1,a.MaxX,a.MinY);Panel(3,a.MinX,a.MaxY,a.MaxX,12);
        }
        private void Panel(int index,float minX,float minY,float maxX,float maxY)
        {
            var t=dangerPanels[index];t.gameObject.SetActive(Hazard.StageIndex>=0 && maxX>minX && maxY>minY);
            t.position=new((minX+maxX)/2,(minY+maxY)/2,2);t.localScale=new(maxX-minX,maxY-minY,1);
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
            diagnostics=Label(root.transform,"TimingDiagnostics",25,88,1230,235,17);
            summary=Label(root.transform,"TimingSummary",25,585,1230,120,18);
            var button=new GameObject("IniciarRaid",typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(root.transform,false);
            var rect=button.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new(.5f,0);rect.pivot=new(.5f,0);rect.anchoredPosition=new(0,145);rect.sizeDelta=new(600,42);
            button.GetComponent<Image>().color=new(.12f,.32f,.24f);start=button.GetComponent<Button>();start.onClick.AddListener(()=>BeginRaid());
            Label(button.transform,"BeginLabel",0,0,600,42,20).text="Iniciar incursión · daño real · 100 HP";
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
        private void OnDestroy() {Context?.Dispose();Destroy(boundaryMaterial);Destroy(warningMaterial);Destroy(dangerMaterial);foreach(var t in dangerPanels) if(t!=null) Destroy(t.GetComponent<MeshFilter>().sharedMesh);}
    }
}
