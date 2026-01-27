using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Dataset;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Enums;

namespace Content.Shared.Humanoid
{
    /// <summary>
    /// Figure out how to name a humanoid with these extensions.
    /// </summary>
    public sealed class NamingSystem : EntitySystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        public string GetName(string species, Gender? gender = null)
        {
            // if they have an old species or whatever just fall back to human I guess?
            // Some downstream is probably gonna have this eventually but then they can deal with fallbacks.
            if (!_prototypeManager.TryIndex(species, out SpeciesPrototype? speciesProto))
            {
                speciesProto = _prototypeManager.Index<SpeciesPrototype>("Human");
                Log.Warning($"Unable to find species {species} for name, falling back to Human");
            }

            switch (speciesProto.Naming)
            {
                case SpeciesNaming.First:
                    return Loc.GetString("namepreset-first",
                        ("first", GetFirstName(speciesProto, gender)));
                // Start of Nyano - Summary: for Oni naming
                case SpeciesNaming.LastNoFirst:
                    return Loc.GetString("namepreset-lastnofirst",
                        ("first", GetFirstName(speciesProto, gender)), ("last", GetLastName(speciesProto)));
                // End of Nyano - Summary: for Oni naming
                case SpeciesNaming.TheFirstofLast:
                    return Loc.GetString("namepreset-thefirstoflast",
                        ("first", GetFirstName(speciesProto, gender)), ("last", GetLastName(speciesProto)));
                case SpeciesNaming.FirstDashFirst:
                    return Loc.GetString("namepreset-firstdashfirst",
                        ("first1", GetFirstName(speciesProto, gender)), ("first2", GetFirstName(speciesProto, gender)));
                case SpeciesNaming.FirstDashLast: // Goobstation
                    return Loc.GetString("namepreset-firstdashlast",
                        ("first", GetFirstName(speciesProto, gender)), ("last", GetLastName(speciesProto)));
                case SpeciesNaming.LastFirst: // DeltaV: Rodentia name scheme
                    return Loc.GetString("namepreset-lastfirst",
                        ("last", GetLastName(speciesProto)), ("first", GetFirstName(speciesProto, gender)));
                case SpeciesNaming.FirstDashMiddleDashLast:
                    return Loc.GetString("namepreset-firstdashmiddledashfirst",
                        ("first", GetFirstName(speciesProto, gender)), ("middle", GetMiddleName(speciesProto)), ("last", GetLastName(speciesProto))); // Exodus-Kidans
                case SpeciesNaming.FirstLast:
                default:
                    return Loc.GetString("namepreset-firstlast",
                        ("first", GetFirstName(speciesProto, gender)), ("last", GetLastName(speciesProto)));
            }
        }

        public string GetFirstName(SpeciesPrototype speciesProto, Gender? gender = null)
        {
            switch (gender)
            {
                // Exodus-Kidans-Start | Crutch to make localized datasets work along with just datasets
                case Gender.Male:
                    {
                        if (_prototypeManager.TryIndex<DatasetPrototype>(speciesProto.MaleFirstNames, out var dataset))
                            return _random.Pick(dataset.Values);

                        if (_prototypeManager.TryIndex<LocalizedDatasetPrototype>(speciesProto.MaleFirstNames, out var localizedDataset))
                            return Loc.GetString(_random.Pick(localizedDataset.Values));

                        throw new Exception($"Can't get prototype for MaleFirstNames of species prototype {speciesProto.ID}");
                    }
                case Gender.Female:
                    {
                        if (_prototypeManager.TryIndex<DatasetPrototype>(speciesProto.FemaleFirstNames, out var dataset))
                            return _random.Pick(dataset.Values);

                        if (_prototypeManager.TryIndex<LocalizedDatasetPrototype>(speciesProto.FemaleFirstNames, out var localizedDataset))
                            return Loc.GetString(_random.Pick(localizedDataset.Values));

                        throw new Exception($"Can't get prototype for FemaleFirstNames of species prototype {speciesProto.ID}");
                    }
                default:
                    if (_random.Prob(0.5f))
                    {
                        if (_prototypeManager.TryIndex<DatasetPrototype>(speciesProto.MaleFirstNames, out var dataset))
                            return _random.Pick(dataset.Values);

                        if (_prototypeManager.TryIndex<LocalizedDatasetPrototype>(speciesProto.MaleFirstNames, out var localizedDataset))
                            return Loc.GetString(_random.Pick(localizedDataset.Values));

                        throw new Exception($"Can't get prototype for MaleFirstNames of species prototype {speciesProto.ID}");
                    }
                    else
                    {
                        if (_prototypeManager.TryIndex<DatasetPrototype>(speciesProto.FemaleFirstNames, out var dataset))
                            return _random.Pick(dataset.Values);

                        if (_prototypeManager.TryIndex<LocalizedDatasetPrototype>(speciesProto.FemaleFirstNames, out var localizedDataset))
                            return Loc.GetString(_random.Pick(localizedDataset.Values));

                        throw new Exception($"Can't get prototype for FemaleFirstNames of species prototype {speciesProto.ID}");
                    }
                // Exodus-Kidans-End
            }
        }

        public string GetLastName(SpeciesPrototype speciesProto)
        {
            // Exodus-Kidans-Start | Crutch to make localized datasets work along with just datasets
            if (_prototypeManager.TryIndex<DatasetPrototype>(speciesProto.LastNames, out var dataset))
                return _random.Pick(dataset.Values);

            if (_prototypeManager.TryIndex<LocalizedDatasetPrototype>(speciesProto.LastNames, out var localizedDataset))
                return Loc.GetString(_random.Pick(localizedDataset.Values));

            throw new Exception($"Can't get prototype for LastNames of species prototype {speciesProto.ID}");
            // Exodus-Kidans-End
        }

        // Exodus-Kidans-Start
        public string GetMiddleName(SpeciesPrototype speciesProto)
        {
            return Loc.GetString(_random.Pick(_prototypeManager.Index(speciesProto.MiddleNames).Values));
        }
        // Exodus-Kidans-End
    }
}
