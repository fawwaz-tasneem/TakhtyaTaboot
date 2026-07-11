namespace TakhtyaTaboot.Util
{
    // The arithmetic of judgment at the darbar, PURE and unit-tested. When the sovereign hears a
    // petition he renders a ruling, and the ruling writes itself into the parties' regard for him
    // (a signed CourtRuling opinion record) and into his own standing. A decisive judgment asserts
    // authority (influence) and lends a little legitimacy but makes an enemy of the loser; a
    // compromise pleases both yet spends influence to do it; turning petitioners away costs
    // legitimacy — a court that will not judge is no court. DarbarPetitionBehavior owns the engine side.
    public static class DarbarCourtMath
    {
        public enum CourtStance { ForPlaintiff, ForDefendant, Compromise, Dismiss }

        public struct CourtOutcome
        {
            public float PlaintiffOpinion; // signed CourtRuling magnitude — the plaintiff's feeling toward the judge
            public float DefendantOpinion; // the defendant's feeling toward the judge
            public int Influence;          // the judge's influence delta
            public float Legitimacy;       // the judge's legitimacy delta
        }

        // A two-party dispute (zamindar vs zamindar, notable vs notable).
        public static CourtOutcome Judge(CourtStance stance)
        {
            switch (stance)
            {
                case CourtStance.ForPlaintiff: return new CourtOutcome { PlaintiffOpinion = +8f, DefendantOpinion = -8f, Influence = +5, Legitimacy = +1f };
                case CourtStance.ForDefendant: return new CourtOutcome { PlaintiffOpinion = -8f, DefendantOpinion = +8f, Influence = +5, Legitimacy = +1f };
                case CourtStance.Compromise:   return new CourtOutcome { PlaintiffOpinion = +3f, DefendantOpinion = +3f, Influence = -5, Legitimacy = +2f };
                case CourtStance.Dismiss:      return new CourtOutcome { PlaintiffOpinion = -5f, DefendantOpinion = -5f, Influence = 0,  Legitimacy = -2f };
                default:                       return new CourtOutcome();
            }
        }

        // A one-party plea with no defendant (a raided village, a hardship). Granting relief wins
        // the deepest gratitude and legitimacy; referring the matter on is neutral; turning them
        // away stings the petitioner and the crown's standing, though it spends no capital.
        public static CourtOutcome JudgePlea(CourtStance stance)
        {
            switch (stance)
            {
                case CourtStance.ForPlaintiff: return new CourtOutcome { PlaintiffOpinion = +10f, Influence = 0,  Legitimacy = +2f };
                case CourtStance.Compromise:   return new CourtOutcome { PlaintiffOpinion = +2f,  Influence = 0,  Legitimacy = +1f };
                case CourtStance.Dismiss:      return new CourtOutcome { PlaintiffOpinion = -6f,  Influence = +2, Legitimacy = -2f };
                default:                       return new CourtOutcome();
            }
        }
    }
}
