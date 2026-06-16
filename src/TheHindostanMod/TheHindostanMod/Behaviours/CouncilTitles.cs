using System;

namespace TakhtyaTaboot
{
    // Culture-appropriate names for the four great offices, in two registers: the king's
    // council (the imperial court) and a lord's council. Post order matches
    // CouncilBehavior.Post: 0 Prime Minister, 1 Commander, 2 Treasurer, 3 Spymaster.
    //
    // Culture mapping (vanilla id -> Hindostan faction):
    //   empire  -> Mughals / Bengal / Hyderabad   (Indo-Persian)
    //   aserai  -> Mysore                          (Indo-Persian)
    //   sturgia -> Durrani Afghans                 (Persianate)
    //   vlandia -> Rajputs
    //   battania-> Marathas
    //   khuzait -> Sikhs
    public static class CouncilTitles
    {
        private static readonly string[] PersianKing = { "Wazir-e-Azam", "Sipah-Salar", "Diwan-i-Kul", "Daroga-e-Khufia" };
        private static readonly string[] PersianLord = { "Diwan", "Bakshi", "Mustaufi", "Harkara" };
        private static readonly string[] RajputKing  = { "Pradhan", "Senapati", "Bhandari", "Mukhbir-Pramukh" };
        private static readonly string[] RajputLord  = { "Diwan", "Senani", "Khazanchi", "Mukhbir" };
        private static readonly string[] MarathaKing = { "Peshwa", "Sar-e-Naubat", "Amatya", "Fadnavis" };
        private static readonly string[] MarathaLord = { "Karbhari", "Senapati", "Phadnis", "Harkara" };
        private static readonly string[] SikhKing    = { "Diwan", "Jathedar-e-Fauj", "Khazanchi", "Khufia-Nawis" };
        private static readonly string[] SikhLord    = { "Diwan", "Jathedar", "Toshakhana", "Harkara" };

        private static readonly string[] RoleEn = { "Prime Minister", "Commander", "Treasurer", "Spymaster" };

        public static string Role(int post) => RoleEn[Clamp(post)];

        // The bare culture name of an office (no English gloss), e.g. for prose.
        public static string Name(string culture, int post, bool king) => Set(culture, king)[Clamp(post)];

        // The displayed title: culture name with an English gloss, e.g. "Peshwa (Prime Minister)".
        public static string Title(string culture, int post, bool king)
            => $"{Name(culture, post, king)} ({Role(post)})";

        private static string[] Set(string culture, bool king)
        {
            switch (culture)
            {
                case "vlandia":  return king ? RajputKing  : RajputLord;
                case "battania": return king ? MarathaKing : MarathaLord;
                case "khuzait":  return king ? SikhKing    : SikhLord;
                default:         return king ? PersianKing : PersianLord; // empire, aserai, sturgia, unknown
            }
        }

        private static int Clamp(int post) => Math.Max(0, Math.Min(3, post));
    }
}
