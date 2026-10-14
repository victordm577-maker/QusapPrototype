using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qusap
{
    // Inclusive X/Y bounds around the participant's Rigidbody centre. Depth is unchanged.
    public readonly struct QusapRaidSafeArea
    {
        public QusapRaidSafeArea(float minX, float minY, float maxX, float maxY)
        {
            if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY)
                || minX >= maxX || minY >= maxY) throw new ArgumentException("Finite, positive safe area required");
            MinX=minX; MinY=minY; MaxX=maxX; MaxY=maxY;
        }
        public float MinX { get; }
        public float MinY { get; }
        public float MaxX { get; }
        public float MaxY { get; }
        public bool Contains(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y)
            && p.x >= MinX && p.x <= MaxX && p.y >= MinY && p.y <= MaxY;
        public bool Contains(QusapRaidSafeArea other) => Contains(new Vector2(other.MinX,other.MinY)) && Contains(new Vector2(other.MaxX,other.MaxY));
        public override string ToString() => $"X [{MinX:0.##}, {MaxX:0.##}] Y [{MinY:0.##}, {MaxY:0.##}]";
    }
    public sealed class QusapRaidHazardStage
    {
        public QusapRaidHazardStage(double startsAt, QusapRaidSafeArea area, float damage, double warningSeconds = 15)
        {
            if (!double.IsFinite(startsAt) || startsAt < 0 || !double.IsFinite(warningSeconds)
                || warningSeconds < 0 || !float.IsFinite(damage) || damage <= 0
                || area.MinX >= area.MaxX || area.MinY >= area.MaxY) throw new ArgumentException("Invalid hazard stage");
            StartsAt=startsAt; Area=area; Damage=damage; WarningSeconds=warningSeconds;
        }
        public double StartsAt { get; }
        public double WarningSeconds { get; }
        public QusapRaidSafeArea Area { get; }
        public float Damage { get; }
    }
    public sealed class QusapRaidHazardSettings
    {
        public QusapRaidHazardSettings(QusapRaidSafeArea initialArea, IEnumerable<QusapRaidHazardStage> stages, double tickSeconds = 1)
        {
            if (!double.IsFinite(tickSeconds) || tickSeconds < .01 || initialArea.MinX >= initialArea.MaxX
                || initialArea.MinY >= initialArea.MaxY) throw new ArgumentException("Valid area and tick interval >= 0.01 s required");
            var copy=stages?.ToArray();
            if (copy == null || copy.Length == 0) throw new ArgumentException("Hazard stages required");
            var previous=initialArea; double time=-1;
            foreach (var stage in copy)
            {
                if (stage == null || stage.StartsAt <= time || !previous.Contains(stage.Area)
                    || (previous.MinX == stage.Area.MinX && previous.MaxX == stage.Area.MaxX
                        && previous.MinY == stage.Area.MinY && previous.MaxY == stage.Area.MaxY))
                    throw new ArgumentException("Ordered, strictly shrinking nested areas required");
                previous=stage.Area; time=stage.StartsAt;
            }
            InitialArea=initialArea; Stages=Array.AsReadOnly(copy); TickSeconds=tickSeconds;
        }
        public QusapRaidSafeArea InitialArea { get; }
        public IReadOnlyList<QusapRaidHazardStage> Stages { get; }
        public double TickSeconds { get; }
        public static QusapRaidHazardSettings Normal() => Create(new double[]{240,300,360,420},15,1);
        public static QusapRaidHazardSettings Diagnostic() => Create(new double[]{6,12,18,24},3,1);
        private static QusapRaidHazardSettings Create(double[] times,double warning,double interval) => new(
            new(-12,-.5f,12,11),new[]{
                new QusapRaidHazardStage(times[0],new(-10,-.5f,10,10),10,warning),
                new QusapRaidHazardStage(times[1],new(-7,-.5f,7,9),12,warning),
                new QusapRaidHazardStage(times[2],new(-4,-.5f,4,8),15,warning),
                new QusapRaidHazardStage(times[3],new(-2.5f,-.5f,2.5f,6),20,warning)},interval);
    }
    public sealed class QusapRaidHazard
    {
        private readonly QusapRaidSession session;
        private readonly IReadOnlyList<QusapRaidInventory> actors;
        private readonly UnityEngine.Object source;
        private long nextTick=1;
        private bool processing;
        private readonly Dictionary<string,float> damageByParticipant = new(StringComparer.Ordinal);
        public QusapRaidHazard(QusapRaidSession session,QusapRaidHazardSettings settings,IReadOnlyList<QusapRaidInventory> actors,UnityEngine.Object source)
        {
            this.session=session ?? throw new ArgumentNullException(nameof(session));
            Settings=settings ?? throw new ArgumentNullException(nameof(settings));
            if(session.Timing == null || settings.Stages.Last().StartsAt >= session.Timing.Settings.DurationSeconds
                || actors == null || actors.Count != session.ParticipantCount || actors.Distinct().Count() != actors.Count
                || actors.Any(a=>a == null || a.Session != session)) throw new ArgumentException("Timed registered roster required");
            this.actors=Array.AsReadOnly(actors.ToArray()); this.source=source;
        }
        public QusapRaidHazardSettings Settings { get; }
        public bool Enabled => session.Timing.IsRunning && !session.IsFinished && !session.Timing.HasExpired;
        public int StageIndexAt(double time)
        { int index=-1; for(int i=0;i<Settings.Stages.Count;i++) if(time >= Settings.Stages[i].StartsAt) index=i; return index; }
        public int StageIndex => StageIndexAt(session.Timing.ElapsedSeconds);
        public QusapRaidSafeArea CurrentArea => StageIndex < 0 ? Settings.InitialArea : Settings.Stages[StageIndex].Area;
        public QusapRaidHazardStage NextStage => StageIndex+1 < Settings.Stages.Count ? Settings.Stages[StageIndex+1] : null;
        public bool WarningActive => Enabled && NextStage != null && session.Timing.ElapsedSeconds >= NextStage.StartsAt-NextStage.WarningSeconds;
        public bool IsExposed(QusapRaidInventory actor) => Enabled && StageIndex >= 0 && Eligible(actor) && !CurrentArea.Contains(Position(actor));
        public long TicksProcessed { get; private set; }
        public int DamageApplications { get; private set; }
        public float DamageApplied { get; private set; }
        public float DamageAppliedTo(string id) => damageByParticipant.TryGetValue(id,out var value) ? value : 0;
        public double LastTickSeconds { get; private set; }
        public event Action<double> TickProcessed;
        private bool Eligible(QusapRaidInventory a) => a != null && a.Session == session && a.CanOperate
            && session.Competition.TryGetOutcome(a.ParticipantId,out var result) && result == QusapRaidParticipantOutcome.Active;
        private static Vector2 Position(QusapRaidInventory a) => a.GetComponent<Rigidbody>().position;
        // Integer tick index avoids frame-rate accumulation drift. Late frames sample current positions
        // for all due ticks; no historical movement is inferred. Expiration discards overdue ticks.
        public void Process()
        {
            if(processing || !Enabled) return;
            processing=true;
            try
            {
                double elapsed=session.Timing.ElapsedSeconds;
                while(Enabled)
                {
                    double tick=Settings.Stages[0].StartsAt+nextTick*Settings.TickSeconds;
                    if(tick > elapsed+1e-9) break;
                    var stage=Settings.Stages[StageIndexAt(tick)];
                    var targets=actors.Where(a=>Eligible(a) && !stage.Area.Contains(Position(a))).ToArray();
                    var health=targets.Select(a=>a.Receiver.CurrentHealth).ToArray();
                    if(targets.Length != 0)
                    {
                        if(!session.ApplyEnvironmentalDamageBatch(targets,stage.Damage,source)) break;
                        for(int i=0;i<targets.Length;i++)
                        {
                            float applied=health[i]-targets[i].Receiver.CurrentHealth;
                            if(applied > 0)
                            {
                                DamageApplications++; DamageApplied+=applied;
                                damageByParticipant[targets[i].ParticipantId]=DamageAppliedTo(targets[i].ParticipantId)+applied;
                            }
                        }
                    }
                    nextTick++; LastTickSeconds=tick; TicksProcessed++;
                    TickProcessed?.Invoke(tick);
                }
            }
            finally { processing=false; }
        }
    }
}
