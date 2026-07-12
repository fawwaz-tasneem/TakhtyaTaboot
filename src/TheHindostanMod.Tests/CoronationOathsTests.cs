using System.Collections.Generic;
using System.Linq;
using TakhtyaTaboot.Util;
using Xunit;

namespace TakhtyaTaboot.Tests
{
    // Pins the book of oaths: every culture speaks in seven distinct voices, the same lord
    // always swears the same oath, an unknown culture falls back to the generic set, and the
    // lord's regard colours the delivery.
    public class CoronationOathsTests
    {
        public static IEnumerable<object[]> Cultures()
            => CoronationOaths.KnownCultures.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(Cultures))]
        public void Every_culture_swears_in_seven_distinct_voices(string culture)
        {
            var variants = Enumerable.Range(0, CoronationOaths.VariantsPerCulture)
                .Select(i => CoronationOaths.Oath(culture, i, CoronationMath.OathRegister.Even))
                .ToList();
            Assert.Equal(CoronationOaths.VariantsPerCulture, variants.Distinct().Count());
            Assert.All(variants, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        }

        [Fact]
        public void All_eight_cultures_of_Hindostan_are_in_the_book()
            => Assert.Equal(8, CoronationOaths.KnownCultures.Count());

        [Fact]
        public void The_same_lord_always_swears_the_same_oath()
        {
            int seed = CoronationOaths.SeedOf("lord_2_1");
            Assert.Equal(CoronationOaths.Oath("sturgia", seed, CoronationMath.OathRegister.Even),
                         CoronationOaths.Oath("sturgia", seed, CoronationMath.OathRegister.Even));
        }

        [Fact]
        public void An_unknown_culture_falls_back_to_the_generic_voice()
            => Assert.False(string.IsNullOrWhiteSpace(CoronationOaths.Oath("firangi_unknown", 3, CoronationMath.OathRegister.Even)));

        [Fact]
        public void The_variant_wheel_wraps()
            => Assert.Equal(CoronationOaths.Oath("empire", 0, CoronationMath.OathRegister.Even),
                            CoronationOaths.Oath("empire", CoronationOaths.VariantsPerCulture, CoronationMath.OathRegister.Even));

        [Fact]
        public void A_negative_seed_is_safe()
            => Assert.False(string.IsNullOrWhiteSpace(CoronationOaths.Oath("empire", -13, CoronationMath.OathRegister.Even)));

        [Fact]
        public void Regard_colours_the_delivery()
        {
            string warm = CoronationOaths.Oath("vlandia", 2, CoronationMath.OathRegister.Warm);
            string even = CoronationOaths.Oath("vlandia", 2, CoronationMath.OathRegister.Even);
            string cold = CoronationOaths.Oath("vlandia", 2, CoronationMath.OathRegister.Cold);
            Assert.NotEqual(warm, even);
            Assert.NotEqual(cold, even);
            Assert.Contains(even, warm); // the body survives the colouring
            Assert.Contains(even, cold);
        }
    }
}
