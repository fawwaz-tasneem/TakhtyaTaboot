using System;
using System.Collections.Generic;
using System.Linq;
using SandBox;
using SandBox.Conversation.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace TakhtyaTaboot.Util
{
    // The coronation procession (playtest round 6: "I as the king should not have to go and ask
    // each person — I should be seated at the throne, and one by one each lord should address
    // me"). Runs only inside a live coronation ceremony (CoronationBehavior stages the lords in
    // the hall and registers this logic): each attending house head, in turn, walks to where
    // the sovereign stands — take your place at the throne and the procession comes to the
    // dais — and addresses him; the conversation is the culture-keyed oath (CoronationOaths).
    // Lords the player already heard are skipped. Every step is guarded: if navigation or the
    // conversation fails for one lord, the procession moves on and the player can still
    // approach anyone by hand (the round-5 behaviour, kept as the floor).
    public sealed class CoronationProcessionLogic : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private enum Step { Gathering, Summoning, Approaching, Speaking, Interlude, Done }

        private Step _step = Step.Gathering;
        private float _clock;
        private Agent _current;
        private readonly Queue<Hero> _pending = new Queue<Hero>();

        private const float GatherSeconds = 5f;     // let the hall finish spawning
        private const float ApproachTimeout = 20f;  // a stuck walker still gets his say
        private const float InterludeSeconds = 2.5f;
        private const float SpeakRange = 2.4f;      // metres from the sovereign

        public override void OnMissionTick(float dt)
        {
            TYTLog.GuardQuiet("Coronation.Procession", () => Tick(dt));
        }

        private void Tick(float dt)
        {
            if (_step == Step.Done) return;
            var ceremony = CoronationBehavior.Instance;
            if (ceremony == null || !ceremony.IsCeremonyLive) { _step = Step.Done; return; }
            _clock += dt;

            switch (_step)
            {
                case Step.Gathering:
                    if (_clock < GatherSeconds) return;
                    foreach (Hero h in ceremony.CeremonyAttendees.Where(h => h != null && !ceremony.HasSworn(h)))
                        _pending.Enqueue(h);
                    Advance(Step.Summoning);
                    return;

                case Step.Summoning:
                    if (ConversationBusy()) return; // the player is already speaking with someone
                    if (_pending.Count == 0) { _step = Step.Done; return; }
                    SummonNext();
                    return;

                case Step.Approaching:
                    if (_current == null || !_current.IsActive()) { Advance(Step.Summoning); return; }
                    if (ConversationBusy()) { Advance(Step.Speaking); return; } // player greeted him first
                    if (DistanceToSovereign(_current) <= SpeakRange || _clock >= ApproachTimeout)
                        BeginAddress();
                    return;

                case Step.Speaking:
                    if (!ConversationBusy()) Advance(Step.Interlude); // his say is said
                    return;

                case Step.Interlude:
                    if (_clock >= InterludeSeconds) Advance(Step.Summoning);
                    return;
            }
        }

        private void Advance(Step next) { _step = next; _clock = 0f; _current = null; }

        private static bool ConversationBusy()
            => Campaign.Current?.ConversationManager?.IsConversationInProgress == true
               || Mission.Current?.Mode == MissionMode.Conversation;

        private static float DistanceToSovereign(Agent a)
            => Agent.Main == null ? float.MaxValue : a.Position.Distance(Agent.Main.Position);

        private void SummonNext()
        {
            if (Agent.Main == null || !Agent.Main.IsActive()) return; // wait for the sovereign to stand
            Hero next = _pending.Dequeue();
            var ceremony = CoronationBehavior.Instance;
            if (next == null || !next.IsAlive || ceremony?.HasSworn(next) == true) return; // sworn meanwhile

            Agent agent = Mission.Current?.Agents?.FirstOrDefault(a =>
                a != null && a.IsHuman && a.IsActive()
                && (a.Character as CharacterObject)?.HeroObject == next);
            if (agent == null) return; // he never made it into the hall; the dais entry covers him

            _current = agent;
            // He walks to two paces before the sovereign — wherever the sovereign has placed
            // himself (stand at the throne and the procession comes to the dais) — and faces him.
            Vec3 mine = Agent.Main.Position;
            Vec3 his = agent.Position;
            Vec3 dir = mine - his;
            Vec2 dir2 = dir.AsVec2;
            if (dir2.Length < 0.01f) { BeginAddress(); return; } // already toe to toe
            dir2 = dir2.Normalized();
            Vec3 stop = mine - new Vec3(dir2.x * 1.8f, dir2.y * 1.8f, 0f);

            var navigator = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            if (navigator == null) { BeginAddress(); return; } // no legs to give him; speak from where he stands
            navigator.SetTargetFrame(new WorldPosition(Mission.Current.Scene, stop), dir2.RotationInRadians, 0.7f);
            Advance(Step.Approaching);
            _current = agent; // Advance clears it; the approacher is this agent
        }

        private void BeginAddress()
        {
            Agent agent = _current;
            Advance(Step.Speaking);
            try
            {
                if (agent != null && agent.IsActive() && !ConversationBusy())
                    MissionConversationLogic.Current?.StartConversation(agent, setActionsInstantly: false);
            }
            catch (Exception e)
            {
                TYTLog.Error("Coronation procession: a lord could not address the throne", e);
                Advance(Step.Interlude); // move on; the player can approach him by hand
            }
        }
    }
}
