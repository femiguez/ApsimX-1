using System;
using System.Collections.Generic;
using System.Linq;
using Models.Core;
using Models.Functions;
using System.Xml.Serialization;
using Models.PMF.Interfaces;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.PMF.Library;
using Models.PMF.Struct;

namespace Models.PMF.Organs
{
    /// <summary>
    /// # [Name]
    /// The leaves are modelled as a set of leaf cohorts and the properties of each of these cohorts are summed to give overall values for the leaf organ.  
    ///   A cohort represents all the leaves of a given main- stem node position including all of the branch leaves appearing at the same time as the given main-stem leaf ([lawless2005wheat]).  
    ///   The number of leaves in each cohort is the product of the number of plants per m<sup>2</sup> and the number of branches per plant.  
    ///   The *Structure* class models the appearance of main-stem leaves and branches.  Once cohorts are initiated the *Leaf* class models the area and biomass dynamics of each.  
    ///   It is assumed all the leaves in each cohort have the same size and biomass properties.  The modelling of the status and function of individual cohorts is delegated to *LeafCohort* classes.  
    /// 
    /// ## Dry Matter Fixation
    /// The most important DM supply from leaf is the photosynthetic fixation supply.  Radiation interception is calculated from
    ///   LAI using an extinction coefficient of:
    /// [Document ExtinctionCoeff]
    /// [Document Photosynthesis]
    /// 
    /// </summary>
    [Serializable]
    [Description("Leaf Class")]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class Leaf : Model, IOrgan, ICanopy, ILeaf, IHasWaterDemand, IArbitration, IRemovableBiomass
    {

        /// <summary>The surface organic matter model</summary>
        [Link]
        public ISurfaceOrganicMatter SurfaceOrganicMatter = null;

        /// <summary>The plant</summary>
        [Link]
        protected Plant parentPlant = null;

        /// <summary>The summary</summary>
        [Link]
        public ISummary Summary = null;

        /// <summary>The met data</summary>
        [Link]
        public IWeather MetData = null;


        /// <summary>Growth Respiration</summary>
        /// [Units("CO_2")]
        public double GrowthRespiration { get; set; }


        /// <summary>Gets the biomass allocated (represented actual growth)</summary>
        [XmlIgnore]
        public Biomass Allocated { get; set; }

        /// <summary>Gets the biomass senesced (transferred from live to dead material)</summary>
        [XmlIgnore]
        public Biomass Senesced { get; set; }

        /// <summary>Gets the DM amount detached (sent to soil/surface organic matter) (g/m2)</summary>
        [XmlIgnore]
        public Biomass Detached { get; set; }

        /// <summary>Gets the DM amount removed from the system (harvested, grazed, etc) (g/m2)</summary>
        [XmlIgnore]
        public Biomass Removed { get; set; }

        /// <summary>Gets the dm supply photosynthesis.</summary>
        [Units("g/m^2")]
        public double DMSupplyPhotosynthesis { get { return DMSupply.Fixation; } }

        /// <summary>The dry matter supply</summary>
        public BiomassSupplyType DMSupply { get; set; }

        /// <summary>The nitrogen supply</summary>
        public BiomassSupplyType NSupply { get;  set; }

        /// <summary>The dry matter demand</summary>
        public BiomassPoolType DMDemand { get; set; }

        /// <summary>Structural nitrogen demand</summary>
        public BiomassPoolType NDemand { get; set; }

        /// <summary>Gets or sets the n fixation cost.</summary>
        public double NFixationCost { get { return 0; } }

        /// <summary>The dry matter potentially being allocated</summary>
        public BiomassPoolType potentialDMAllocation { get; set; }

        #region Canopy interface
        /// <summary>Gets the canopy. Should return null if no canopy present.</summary>
        public string CanopyType { get { return parentPlant.CropType; } }

        /// <summary>Albedo.</summary>
        [Description("Canopy Albedo")]
        public double Albedo { get; set; }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("Daily maximum stomatal conductance(m/s)")]
        public double Gsmax {
            get
            {
                return Gsmax350*FRGR*StomatalConductanceCO2Modifier.Value();
            }
        }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("Maximum stomatal conductance at CO2 concentration of 350 ppm (m/s)")]
        public double Gsmax350 { get; set; }

        /// <summary>Gets or sets the R50.</summary>
        [Description("R50: solar radiation at which stomatal conductance decreases to 50% (W/m^2)")]
        public double R50 { get; set; }

        /// <summary>Gets the LAI</summary>
        [Units("m^2/m^2")]
        public double LAI
        {
            get
            {
                int MM2ToM2 = 1000000; // Conversion of mm2 to m2
                foreach (LeafCohort L in Leaves)
                    if (Double.IsNaN(L.LiveArea))
                        throw new Exception("LiveArea of leaf cohort " + L.Name + " is Nan");
                return Leaves.Sum(x => x.LiveArea) / MM2ToM2;
            }
        }

        /// <summary>Gets the LAI live + dead (m^2/m^2)</summary>
        public double LAITotal { get { return LAI + LAIDead; } }

        /// <summary>Gets the cover green.</summary>
        [Units("0-1")]
        public double CoverGreen
        {
            get
            {
                if (parentPlant != null && parentPlant.IsAlive)
                    return Math.Min(MaxCover * (1.0 - Math.Exp(-ExtinctionCoeff.Value() * LAI / MaxCover)), 0.999999999);
                return 0;
            }
        }

        /// <summary>Gets the cover total.</summary>
        [Units("0-1")]
        public double CoverTotal
        {
            get
            {
                if (parentPlant != null && parentPlant.IsAlive)
                    return 1.0 - (1 - CoverGreen) * (1 - CoverDead);
                return 0;
            }
        }

        /// <summary>Gets the height.</summary>
        [Units("mm")]
        public double Height { get { return Structure.Height; } }

        /// <summary>Gets the depth.</summary>
        [Units("mm")]
        public double Depth { get { return Structure.Height; } }

        /// <summary>Gets the width of the canopy (mm).</summary>
        public double Width { get { return 0; } }

        /// <summary>Gets  FRGR.</summary>
        [Description("Relative growth rate for calculating stomata conductance which fed the Penman-Monteith function")]
        [Units("0-1")]
        public double FRGR { get; set; }

        private double _PotentialEP;
        /// <summary>Sets the potential evapotranspiration. Set by MICROCLIMATE.</summary>
        [Units("mm")]
        public double PotentialEP
        {
            get { return _PotentialEP; }
            set { _PotentialEP = value;}
        }

        /// <summary>Sets the actual water demand.</summary>
        [Units("mm")]
        public double WaterDemand { get; set; }

        /// <summary>Sets the light profile. Set by MICROCLIMATE.</summary>
        public CanopyEnergyBalanceInterceptionlayerType[] LightProfile { get; set; }
        #endregion

        #region Has Water Demand Interface

        /// <summary>Calculates the water demand.</summary>
        public double CalculateWaterDemand()
        {
            return WaterDemand;
        }

        /// <summary>Gets or sets the water allocation.</summary>
        [XmlIgnore]
        public double WaterAllocation { get; set; }

        #endregion

        #region Links
        /// <summary>The structure</summary>
        [Link]
        public Structure Structure = null;
        #endregion

        #region Structures
        /// <summary>
        /// # Potential Leaf Area index
        /// Leaf area index is calculated as the sum of the area of each cohort of leaves 
        /// The appearance of a new cohort of leaves occurs each time Structure.LeafTipsAppeared increases by one.
        /// From tip appearance the area of each cohort will increase for a certian number of degree days defined by the <i>GrowthDuration</i>
        /// [Document GrowthDuration]
        /// 
        /// If no stress occurs the leaves will reach a Maximum area (<i>MaxArea</i>) at the end of the <i>GrowthDuration</i>.
        /// The <i>MaxArea</i> is defined by:
        /// [Document MaxArea]
        /// 
        /// In the absence of stress the leaf will remain at <i>MaxArea</i> for a number of degree days
        /// set by the <i>LagDuration</i> and then area will senesce to zero at the end of the <i>SenescenceDuration</i>
        /// [Document LagDuration]
        /// [Document SenescenceDuration]
        /// 
        /// Mutual shading can cause premature senescence of cohorts if the leaf area above them becomes too great.
        /// Each cohort models the proportion of its area that is lost to shade induced senescence each day as:
        /// [Document ShadeInducedSenescenceRate]
        /// 
        /// # Stress effects on Leaf Area Index
        /// Stress reduces leaf area in a number of ways.
        /// Firstly, stress occuring prior to the appearance of the cohort can reduce cell division, so reducing the maximum leaf size.
        /// Leaf captures this by multiplying the <i>MaxSize</i> of each cohort by a <i>CellDivisionStress</i> factor which is calculated as:
        /// [Document CellDivisionStress]
        /// 
        /// Leaf.FN quantifys the N stress status of the plant and represents the concentration of metabolic N relative the maximum potentil metabolic N content of the leaf
        /// calculated as (<i>Leaf.NConc - MinimumNConc</i>)/(<i>CriticalNConc - MinimumNConc</i>).
        /// 
        /// Leaf.FW quantifies water stress and is
        /// calculated as <i>Leaf.Transpiration</i>/<i>Leaf.WaterDemand</i>, where <i>Leaf.Transpiration</i> is the minimum of <i>Leaf.WaterDemand</i> and <i>Root.WaterUptake</i>
        ///
        /// Stress during the <i>GrowthDuration</i> of the cohort reduces the size increase of the cohort by
        /// multiplying the potential increase by a <i>ExpansionStress</i> factor:
        /// [Document ExpansionStress]
        /// 
        /// Stresses can also acellerate the onset and rate of senescence in a number of ways.
        /// Nitrogen shortage will cause N to be retranslocated out of lower order leaves to support the expansion of higher order leaves and other organs
        /// When this happens the lower order cohorts will have their area reduced in proportion to the amount of N that is remobilised out of them.
        ///
        /// Water stress hastens senescence by increasing the rate of thermal time accumulation in the lag and senescence phases.
        /// This is done by multiplying thermal time accumulation by <i>DroughtInducedLagAcceleration</i> and <i>DroughtInducedSenescenceAcceleration</i> factors, respectively:
        /// [Document DroughtInducedLagAcceleration]
        /// [Document DroughtInducedSenAcceleration]
        /// 
        /// # Dry matter Demand
        /// Leaf calculates the DM demand from each cohort as a function of the potential size increment (DeltaPotentialArea) an specific leaf area bounds.
        /// Under non stressed conditions the demand for non-storage DM is calculated as <i>DeltaPotentialArea</i> divided by the mean of <i>SpecificLeafAreaMax</i> and <i>SpecificLeafAreaMin</i>.
        /// Under stressed conditions it is calculated as <i>DeltaWaterConstrainedArea</i> divided by <i>SpecificLeafAreaMin</i>.
        /// [Document SpecificLeafAreaMax]
        /// [Document SpecificLeafAreaMin]
        /// 
        /// Non-storage DM Demand is then seperated into structural and metabolic DM demands using the <i>StructuralFraction</i>:
        /// [Document StructuralFraction]
        /// 
        /// The storage DM demand is calculated from the sum of metabolic and structural DM (including todays demands)
        /// multiplied by a <i>NonStructuralFraction</i>:
        /// [Document NonStructuralFraction]
        /// 
        /// # Nitrogen Demand
        /// 
        /// Leaf calculates the N demand from each cohort as a function of the potential DM increment and N concentration bounds.
        /// Structural N demand = <i>PotentialStructuralDMAllocation</i> * <i>MinimumNConc</i> where:
        /// [Document MinimumNConc]
        /// 
        /// Metabolic N demand is calculated as <i>PotentialMetabolicDMAllocation</i> * (<i>CriticalNConc</i> - <i>MinimumNConc</i>) where:
        /// [Document CriticalNConc]
        /// 
        /// Storage N demand is calculated as the sum of metabolic and structural wt (including todays demands)
        /// multiplied by <i>LuxaryNconc</i> (<i>MaximumNConc</i> - <i>CriticalNConc</i>) less the amount of storage N already present.  <i>MaximumNConc</i> is given by:
        /// [Document MaximumNConc]
        ///
        /// # Drymatter supply
        /// In additon to photosynthesis, the leaf can also supply DM by reallocation of senescing DM and retranslocation of storgage DM:
        /// Reallocation supply is a proportion of the metabolic and non-structural DM that would be senesced each day where the proportion is set by:
        /// [Document DMReallocationFactor]
        /// Retranslocation supply is calculated as a proportion of the amount of storage DM in each cohort where the proportion is set by :
        /// [Document DMRetranslocationFactor]
        ///
        /// # Nitrogen supply
        /// Nitrogen supply from the leaf comes from the reallocation of metabolic and storage N in senescing material
        /// and the retranslocation of metabolic and storage N.  Reallocation supply is a proportion of the Metabolic and Storage DM that would be senesced each day where the proportion is set by:
        /// [Document NReallocationFactor]
        /// Retranslocation supply is calculated as a proportion of the amount of storage and metabolic N in each cohort where the proportion is set by :
        /// [Document NRetranslocationFactor]
        /// </summary>
        [Serializable]
        public class LeafCohortParameters : Model
        {
            /// <summary>The maximum area</summary>
            [Link]
            [Units("mm2")]
            public IFunction MaxArea = null;
            /// <summary>The growth duration</summary>
            [Link]
            [Units("deg day")]
            public IFunction GrowthDuration = null;
            /// <summary>The lag duration</summary>
            [Link]
            [Units("deg day")]
            public IFunction LagDuration = null;
            /// <summary>The senescence duration</summary>
            [Link]
            [Units("deg day")]
            public IFunction SenescenceDuration = null;
            /// <summary>The detachment lag duration</summary>
            [Link]
            [Units("deg day")]
            public IFunction DetachmentLagDuration = null;
            /// <summary>The detachment duration</summary>
            [Link]
            [Units("deg day")]
            public IFunction DetachmentDuration = null;
            /// <summary>The specific leaf area maximum</summary>
            [Link]
            public IFunction SpecificLeafAreaMax = null;
            /// <summary>The specific leaf area minimum</summary>
            [Link]
            public IFunction SpecificLeafAreaMin = null;
            /// <summary>The structural fraction</summary>
            [Link]
            public IFunction StructuralFraction = null;
            /// <summary>The maximum n conc</summary>
            [Link]
            public IFunction MaximumNConc = null;
            /// <summary>The minimum n conc</summary>
            [Link]
            public IFunction MinimumNConc = null;
            /// <summary>The initial n conc</summary>
            [Link]
            public IFunction InitialNConc = null;
            /// <summary>The n reallocation factor</summary>
            [Link]
            public IFunction NReallocationFactor = null;
            /// <summary>The dm reallocation factor</summary>
            [Link]
            public IFunction DMReallocationFactor = null;
            /// <summary>The n retranslocation factor</summary>
            [Link]
            public IFunction NRetranslocationFactor = null;
            /// <summary>The expansion stress</summary>
            [Link]
            public IFunction ExpansionStress = null;

            /// <summary>The expansion stress</summary>
            public double ExpansionStressValue { get; set; }

            /// <summary>The critical n conc</summary>
            [Link]
            public IFunction CriticalNConc = null;
            /// <summary>The dm retranslocation factor</summary>
            [Link]
            public IFunction DMRetranslocationFactor = null;
            /// <summary>The shade induced senescence rate</summary>
            [Link]
            public IFunction ShadeInducedSenescenceRate = null;
            /// <summary>The drought induced reduction of lag phase through acceleration of tt accumulation by the cohort during this phase</summary>
            [Link]
            public IFunction DroughtInducedLagAcceleration = null;
            /// <summary>The drought induced reduction of senescence phase through acceleration of tt accumulation by the cohort during this phase</summary>
            [Link]
            public IFunction DroughtInducedSenAcceleration = null;
            /// <summary>The non structural fraction</summary>
            [Link]
            public IFunction StorageFraction = null;
            /// <summary>The cell division stress</summary>
            [Link]
            public IFunction CellDivisionStress = null;
            /// <summary>The Shape of the sigmoidal function of leaf area increase</summary>
            [Link]
            public IFunction LeafSizeShapeParameter = null;
            /// <summary>The size of leaves on senessing tillers relative to the dominant tillers in that cohort</summary>
            [Link]
            public IFunction SenessingLeafRelativeSize = null;
            /// <summary>The proportion of mass that is respired each day</summary>
            [Link]
            public IFunction MaintenanceRespirationFunction = null;
            /// <summary>Modify leaf size by age</summary>
            [Link]
            public IFunction LeafSizeAgeMultiplier = null;
            /// <summary>Modify lag duration by age</summary>
            [Link]
            public IFunction LagDurationAgeMultiplier = null;
            /// <summary>Modify senescence duration by age</summary>
            [Link]
            public IFunction SenescenceDurationAgeMultiplier = null;
            /// <summary>The cost for remobilisation</summary>
            [Link]
            public IFunction RemobilisationCost = null;
        }
        #endregion

        #region Parameters

        /// <summary>The initial leaves</summary>
        [DoNotDocument]
        private LeafCohort[] InitialLeaves;
        /// <summary>The leaf cohort parameters</summary>
        [Link] LeafCohortParameters CohortParameters = null;
        /// <summary>The photosynthesis</summary>
        [Link] IFunction Photosynthesis = null;
        /// <summary>The Fractional Growth Rate</summary>
        [Link]
        IFunction FRGRFunction = null;
        /// <summary>The effect of CO2 on stomatal conductance</summary>
        [Link]
        IFunction StomatalConductanceCO2Modifier = null;

        /// <summary>The thermal time</summary>
        [Link]
        public IFunction ThermalTime = null;
        /// <summary>The extinction coeff</summary>
        [Link]
        IFunction ExtinctionCoeff = null;
        /// <summary>The frost fraction</summary>
        [Link]
        IFunction FrostFraction = null;

        /// <summary>The structural fraction</summary>
        [Link]
        IFunction StructuralFraction = null;
        /// <summary>The dm demand function</summary>
        [Link(IsOptional = true)]
        IFunction DMDemandFunction = null;
        /// <summary>The dm demand function</summary>
        [Link]
        IFunction DMConversionEfficiency = null;

        /// <summary>Carbon concentration</summary>
        /// [Units("-")]
        [Link]
        IFunction CarbonConcentration = null;

        /// <summary>Link to biomass removal model</summary>
        [ChildLink]
        public BiomassRemoval biomassRemovalModel = null;

        /// <summary>Gets or sets the k dead.</summary>
        [Description("Extinction Coefficient (Dead)")]
        public double KDead { get; set; }

        /// <summary>Gets or sets the maximum number of leaves on the main stem</summary>
        [Description("Maximum number of Main-Stem leaves")]
        public int MaximumMainStemLeafNumber { get; set; }

        /// <summary>Do we need to recalculate (expensive operation) live and dead</summary>
        private bool needToRecalculateLiveDead = true;
        private Biomass liveBiomass = new Biomass();
        private Biomass deadBiomass = new Biomass();
        #endregion

        #region States

        /// <summary>The leaves</summary>
        public List<LeafCohort> Leaves = new List<LeafCohort>();

        /// <summary>Initialise all state variables.</summary>
        public double CurrentExpandingLeaf = 0;
        /// <summary>The start fraction expanded</summary>
        public double StartFractionExpanded = 0;
        /// <summary>The fraction nextleaf expanded</summary>
        public double FractionNextleafExpanded = 0;
        /// <summary>The dead nodes yesterday</summary>
        public double DeadNodesYesterday = 0;//Fixme This needs to be set somewhere
        #endregion

        #region Outputs
        //Note on naming convention.  
        //Variables that represent the number of units per meter of area these are called population (Popn suffix) variables 
        //Variables that represent the number of leaf cohorts (integer) in a particular state on an individual main-stem are cohort variables (CohortNo suffix)
        //Variables that represent the number of primordia or nodes (double) in a particular state on an individual mainstem are called number variables (e.g NodeNo or PrimordiaNo suffix)
        //Variables that the number of leaves on a plant or a primary bud have Plant or Primary bud prefixes
        
        /// <summary>Gets a value indicating whether the biomass is above ground or not</summary>
        public bool IsAboveGround { get { return true; } }

        /// <summary>Gets the total (live + dead) N concentration (g/g)</summary>
        [XmlIgnore]
        public double Nconc
        {
            get
            {
                if (Wt > 0.0)
                    return N / Wt;
                else
                    return 0.0;
            }
        }


        /// <summary>Return the</summary>
        public double CohortCurrentRankCoverAbove
        {
            get
            {
                if (CurrentRank > Leaves.Count)
                    throw new ApsimXException(this, "Curent Rank is greater than the number of leaves appeared when trying to determine CoverAbove this cohort");
                if (CurrentRank <= 0)
                    return 0;
                return Leaves[CurrentRank - 1].CoverAbove;
            }
        }

        /// <summary>
        /// The number of leaves that have visiable tips on the day of emergence
        /// </summary>
        public int TipsAtEmergence { get; set; }

        /// <summary>
        /// The number of leaf cohorts to initialised
        /// </summary>
        public int CohortsAtInitialisation { get; set; }

        /// <Summary>Spcific leaf nitrogen</Summary>
        [Description("Specific leaf nitrogen")]
        [Units("g/m2")]
        public double SpecificNitrogen
        {
            get
            {
                if (Math.Abs(LAI) < double.Epsilon)
                {
                    return 0;
                }
                return (Live.N / LAI);
            }
        }
        /// <summary>Gets or sets the fraction died.</summary>
        public double FractionDied { get; set; }
        /// <summary>
        /// Gets a value indicating whether [cohorts initialised].
        /// </summary>
        public bool CohortsInitialised { get { return Leaves.Count > 0; } }

        /// <summary>The maximum cover</summary>
        [Description("Max cover")]
        [Units("max units")]
        public double MaxCover;

        /// <summary>The number of cohorts initiated that have not yet emerged</summary>
        [Description("The number of cohorts initiated that have not yet emerged")]
        public int ApicalCohortNo { get { return InitialisedCohortNo - AppearedCohortNo; } }
        
        /// <summary>Gets the initialised cohort no.</summary>
        [Description("Number of leaf cohort objects that have been initialised")]
        public int InitialisedCohortNo { get { return Leaves.Count(l => l.IsInitialised);} }

        /// <summary>Gets the appeared cohort no.</summary>
        [Description("Number of leaf cohort that have appeared")]
        public int AppearedCohortNo { get { return Leaves.Count(l => l.IsAppeared); } }

        /// <summary>Gets the expanding cohort no.</summary>
        [Description("Number of leaf cohorts that have appeared but not yet fully expanded")]
        public int ExpandingCohortNo { get { return Leaves.Count(l => l.IsGrowing); } }

        /// <summary>Gets the expanded cohort no.</summary>
        [Description("Number of leaf cohorts that are fully expanded")]
        public int ExpandedCohortNo { get { return Leaves.Count(l => l.IsFullyExpanded); } }

        /// <summary>Gets the green cohort no.</summary>
        [Description("Number of leaf cohorts that are have expanded but not yet fully senesced")]
        public int GreenCohortNo { get { return Leaves.Count(l => l.IsGreen); } }

        /// <summary>Gets the green cohort no.</summary>
        [Description("Number of leaf cohorts that are have expanded but 50% fully senesced")]
        public int GreenCohortNoHalfSenescence { get {
                int count = 0;
                foreach (LeafCohort l in Leaves)
                {
                    if (l.Age >= 0 && l.Age < l.LagDuration + l.GrowthDuration + l.SenescenceDuration / 2)
                        count++;
                }
                return count;
            } }

        /// <summary>Gets the senescing cohort no.</summary>
        [Description("Number of leaf cohorts that are Senescing")]
        public int SenescingCohortNo { get { return Leaves.Count(l => l.IsSenescing); } }

        /// <summary>Gets the dead cohort no.</summary>
        [Description("Number of leaf cohorts that have fully Senesced")]
        public double DeadCohortNo { get { return Math.Min(Leaves.Count(l => l.IsDead), Structure.finalLeafNumber.Value()); } }

        /// <summary>Gets the plant appeared green leaf no.</summary>
        [Units("/plant")]
        [Description("Number of appeared leaves per plant that have appeared but not yet fully senesced on each plant")]
        public double PlantAppearedGreenLeafNo
        {
            get
            {
                return Leaves.Where(l => l.IsAppeared && !l.Finished).Sum(l => l.CohortPopulation) / parentPlant.Population;
            }
        }

        /// <summary>Gets the plant appeared green leaf no. (matching with observation)</summary>
        [Units("/plant")]
        [Description("Number of appeared leaves per plant that have appeared but 50% senesced on each plant")]
        public double PlantAppearedGreenLeafNoHalfSenescence
        {
            get
            {
                return Leaves.Where(l => l.Age >= 0 && l.Age < l.LagDuration + l.GrowthDuration + l.SenescenceDuration / 2).Sum(l => l.CohortPopulation) / parentPlant.Population;
            }
        }

        /// <summary>Gets the plant appeared leaf no.</summary>
        [Units("/plant")]
        [Description("Number of leaves per plant that have appeared")]
        public double PlantAppearedLeafNo
        {
            get { return Leaves.Where(l => l.IsAppeared).Sum(l => l.CohortPopulation); }
        }

        /// <summary>Gets the plant senesced leaf no.</summary>
        [Units("/plant")]
        [Description("Number of leaves per plant that have senesced")]
        public double PlantsenescedLeafNo
        {
            get { return PlantAppearedLeafNo / parentPlant.Population - PlantAppearedGreenLeafNo; }
        }

        /// <summary>Gets the lai dead.</summary>
        [Units("m^2/m^2")]
        public double LAIDead
        {
            get { return Leaves.Sum(l => l.DeadArea) / 1000000; }
        }

        /// <summary>Gets the cohort live.</summary>
        [XmlIgnore]
        [Units("g/m^2")]
        public Biomass Live
        {
            get
            {
                RecalculateLiveDead();
                return liveBiomass;
            }

        }

        /// <summary>Gets the cohort dead.</summary>
        [XmlIgnore]
        [Units("g/m^2")]
        public Biomass Dead
        {
            get
            {
                RecalculateLiveDead();
                return deadBiomass;
            }
        }

        /// <summary>Recalculate live and dead biomass if necessary</summary>
        private void RecalculateLiveDead()
        {
            if (needToRecalculateLiveDead)
            {
                needToRecalculateLiveDead = false;
                liveBiomass.Clear();
                deadBiomass.Clear();
                foreach (LeafCohort L in Leaves)
                {
                    liveBiomass.Add(L.Live);
                    deadBiomass.Add(L.Dead);
                }
            }
        }

        /// <summary>Gets the cover dead.</summary>
        [Units("0-1")]
        public double CoverDead { get { return 1.0 - Math.Exp(-KDead * LAIDead); } }

        /// <summary>Gets the RAD int tot.</summary>
        [Units("MJ/m^2/day")]
        [Description("This is the intercepted radiation value that is passed to the RUE class to calculate DM supply")]
        public double RadIntTot
        {
            get
            {
                if (LightProfile != null)
                {
                    double TotalRadn = 0;
                    for (int i = 0; i < LightProfile.Length; i++)
                        TotalRadn += LightProfile[i].amount;
                    return TotalRadn;                    
                }
                else
                    return CoverGreen * MetData.Radn;
            }
        }

        /// <summary>Gets the specific area.</summary>
        [Units("mm^2/g")]
        public double SpecificArea { get { return MathUtilities.Divide(LAI * 1000000, Live.Wt, 0); } }

        /// <summary>
        /// Returns the relative expansion of the next leaf to produce its ligule
        /// </summary>
        public double NextExpandingLeafProportion
        {
            get
            {
                if (parentPlant.IsEmerged)
                    if (ExpandedCohortNo < InitialisedCohortNo)
                        if (Leaves[(int)ExpandedCohortNo].Age > 0)
                            if (AppearedCohortNo < InitialisedCohortNo)
                                return Math.Min(1,
                                    Leaves[(int)ExpandedCohortNo].Age / Leaves[(int)ExpandedCohortNo].GrowthDuration);
                            else
                                return Math.Min(1,
                                    Leaves[(int)ExpandedCohortNo].Age / Leaves[(int)ExpandedCohortNo].GrowthDuration *
                                    Structure.NextLeafProportion);
                        else
                            return 0;
                    else
                        return Structure.NextLeafProportion - 1;
                return 0;
            }
        }
        
        /// <summary>Gets the DeltaPotentialArea</summary>
        [XmlIgnore]
        [Units("mm2")]
        public double[] DeltaPotentialArea
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];
                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsGrowing)
                        values[i] = L.DeltaPotentialArea;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }


        /// <summary>Gets the DeltaStressConstrainedArea</summary>
        [XmlIgnore]
        [Units("mm2")]
        public double [] DeltaStressConstrainedArea
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];
                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsGrowing)
                        values[i] += L.DeltaStressConstrainedArea;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }

        /// <summary>Gets the DeltaCarbonConstrainedArea</summary>
        [XmlIgnore]
        [Units("mm2")]
        public double [] DeltaCarbonConstrainedArea
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];
                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsGrowing)
                        values[i] += L.DeltaCarbonConstrainedArea;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }




        /// <summary>Gets the DeltaCarbonConstrainedArea</summary>
        [XmlIgnore]
        [Units("mm2")]
        public double[] CohortStructuralDMDemand
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];
                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsGrowing)
                        values[i] += L.StructuralDMDemand;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }


        /// <summary>Gets the DeltaCarbonConstrainedArea</summary>
        [XmlIgnore]
        [Units("mm2")]
        public double[] CohortMetabolicDMDemand
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];
                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsGrowing)
                        values[i] += L.MetabolicDMDemand;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }

        /// <summary>Gets the DeltaCarbonConstrainedArea</summary>
        [XmlIgnore]
        [Units("mm2")]
        public double[] CohortStorageDMDemand
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];
                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsGrowing)
                        values[i] += L.StorageDMDemand;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }

        /// <summary>Gets the cohort population.</summary>
        [XmlIgnore]
        [Units("mm3")]
        public double[] CohortPopulation
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsAppeared)
                        values[i] = L.CohortPopulation;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }


        /// <summary>Gets the size of the cohort.</summary>
        [XmlIgnore]
        [Units("mm3")]
        public double[] CohortSize
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    if (L.IsAppeared)
                        values[i] = L.LiveArea / L.CohortPopulation;
                    else values[i] = 0;
                    i++;
                }
                return values;
            }
        }

        /// <summary>Gets the cohort area.</summary>
        [Units("mm2")]
        public double[] CohortArea
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.LiveArea;
                    i++;
                }
                return values;
            }
        }

        /// <summary>Gets the maximum size of the cohort.</summary>
        [Units("mm2")]
        public double[] CohortMaxSize
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.MaxLiveArea / L.MaxCohortPopulation;
                    i++;
                }
                return values;
            }
        }

        /// <summary>Gets lag duration</summary>
        [Units("oCd")]
        public double[] CohortLagDuration
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.LagDuration;
                    ;
                    i++;
                }
                return values;
            }
        }


        /// <summary>Gets fraction of leaf senescence.</summary>
        [Units("")]
        public double[] CohortSenescedFrac
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.SenescedFrac;
                    ;
                    i++;
                }
                return values;
            }
        }


        /// <summary>Gets the cohort sla.</summary>
        [Units("mm2/g")]
        public double[] CohortSLA
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.SpecificArea;
                    i++;
                }
                return values;
            }
        }
        /// <summary>Gets the cohort MaxArea.</summary> 
        [Units("mm2")]
        public double[] CohortMaxArea
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.MaxArea;
                    i++;
                }
                return values;
            }
        }

        
        /// <summary>Gets the cohort Wt.</summary> 
        [Units("mm2")]
        public double[] CohortLiveWt
        {
            get
            {
                int i = 0;
                double[] values = new double[MaximumMainStemLeafNumber];

                foreach (LeafCohort L in Leaves)
                {
                    values[i] = L.Live.Wt;
                    i++;
                }
                return values;
            }
        }
        //General Leaf State variables
        /// <summary>Returns the area of the largest leaf.</summary>
        /// <value>The area of the largest leaf</value>
        [Units("mm2")]
        public double AreaLargestLeaf
        {
            get
            {
                double LLA = 0;
                foreach (LeafCohort L in Leaves)
                {
                    LLA = Math.Max(LLA, L.MaxArea);
                }

                return LLA;
            }
        }

        /// <summary>Gets the live stem  number to represent the observed stem numbers in an experiment.</summary>
        /// <value>Stem number.</value>
        [Units("0-1")]
        [XmlIgnore]
        [Description("In the field experiment, we count stem number according whether a stem number has a green leaf. A green leaf is definied as a leaf has more than half green part.")]
        public double LiveStemNumber
        {
            get
            {
                double sn = 0;

                foreach (LeafCohort L in Leaves)
                {
                    sn = Math.Max(sn, L.LiveStemNumber(CohortParameters));
                }
                return sn;

            }
        }

        /// <summary>Gets the live n conc.</summary>
        [Units("g/g")]
        public double LiveNConc { get { return Live.NConc; } }

        /// <summary>Gets the potential growth.</summary>
        [Units("g/m^2")]
        public double PotentialGrowth { get { return DMDemand.Structural; } }

        /// <summary>Gets the transpiration.</summary>
        [Units("mm")]
        public double Transpiration { get { return WaterAllocation; } }
        /// <summary>
        /// The amount of mass lost to maintenance respiration
        /// </summary>
        public double MaintenanceRespiration
        {
            get { return Leaves.Sum(l => l.MaintenanceRespiration); }
        }

        /// <summary>Gets the fw.</summary>
        [Units("0-1")]
        public double Fw { get { return MathUtilities.Divide(WaterAllocation, PotentialEP, 1); } }

        /// <summary>Gets the function.</summary>
        [Units("0-1")]
        public double Fn
        {
            get
            {
                if (CohortParameters == null)
                    return 1;

                double f;
                double functionalNConc = (CohortParameters.CriticalNConc.Value() -
                                          CohortParameters.MinimumNConc.Value() * CohortParameters.StructuralFraction.Value()) *
                                         (1 / (1 - CohortParameters.StructuralFraction.Value()));
                if (functionalNConc <= 0)
                    f = 1;
                else
                    f = Math.Max(0.0, Math.Min(Live.MetabolicNConc / functionalNConc, 1.0));

                return f;
            }
        }

        /// <summary>Total apex number in plant.</summary>
        [Description("Total apex number in plant")]
        public double ApexNum
        {
            get
            {
                if (Leaves.Count == 0 || Leaves.Last().Apex == null)
                    return 0;
                else
                    return Leaves.Last().Apex.Number;
            }
        }
         
        /// <summary>Apex group size in plant</summary>
        [Description("Apex group size in plant")]
        public double[] ApexGroupSize
        {
            get
            {
                if (Leaves.Count == 0 || Leaves.Last().Apex == null)
                    return new double[0];
                else
                    return Leaves.Last().Apex.GroupSize;
            }
        }

        /// <summary>Apex group age in plant</summary>
        [Description("Apex group age in plant")]
        public double[] ApexGroupAge
        {
            get
            {
                if (Leaves.Count == 0 || Leaves.Last().Apex == null)
                    return new double[0];
                else
                    return Leaves.Last().Apex.GroupAge;
            }
        }

        #endregion

        #region Functions

        /// <summary>1 based rank of the current leaf.</summary>
        private int CurrentRank { get; set; }

        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            if (!parentPlant.IsEmerged)
                return;

            if (FrostFraction.Value() > 0)
                foreach (LeafCohort l in Leaves)
                    l.DoFrost(FrostFraction.Value());

            CohortParameters.ExpansionStressValue = CohortParameters.ExpansionStress.Value();
            bool nextExpandingLeaf = false;
            double thermalTime = ThermalTime.Value();

            foreach (LeafCohort L in Leaves)
            {
                CurrentRank = L.Rank;
                L.DoPotentialGrowth(thermalTime, CohortParameters);
                needToRecalculateLiveDead = true;
                if ((L.IsFullyExpanded == false) && (nextExpandingLeaf == false))
                {
                    nextExpandingLeaf = true;
                    if (CurrentExpandingLeaf != L.Rank)
                    {
                        CurrentExpandingLeaf = L.Rank;
                        StartFractionExpanded = L.FractionExpanded;
                    }
                    FractionNextleafExpanded = (L.FractionExpanded - StartFractionExpanded) / (1 - StartFractionExpanded);
                }
            }
            FRGR = FRGRFunction.Value();
        }

        /// <summary>Clears this instance.</summary>
        public void Reset()
        {
            Leaves = new List<LeafCohort>();
            needToRecalculateLiveDead = true;
            WaterAllocation = 0;
            CohortsAtInitialisation = 0;
            TipsAtEmergence = 0;
            Structure.TipToAppear = 0;
            DMSupply.Clear();
            DMDemand.Clear();
            NSupply.Clear();
            NDemand.Clear();
        }
        /// <summary>Initialises the cohorts.</summary>
        [EventSubscribe("InitialiseLeafCohorts")]
        private void OnInitialiseLeafCohorts(object sender, EventArgs e) //This sets up cohorts (eg at germination)
        {
            Leaves = new List<LeafCohort>();
            foreach (LeafCohort Leaf in InitialLeaves)
            {
                LeafCohort NewLeaf = Leaf.Clone();
                Leaves.Add(NewLeaf);
                needToRecalculateLiveDead = true;
            }

            foreach (LeafCohort Leaf in Leaves)
            {
                CohortsAtInitialisation += 1;
                if (Leaf.Area > 0)
                    TipsAtEmergence += 1;
                Leaf.DoInitialisation();
            }
        }

        /// <summary>Method to initialise new cohorts</summary>
        [EventSubscribe("AddLeafCohort")]
        private void OnAddLeafCohort(object sender, CohortInitParams InitParams)
        {
            if (CohortsInitialised == false)
                throw new Exception("Trying to initialse new cohorts prior to InitialStage.  Check the InitialStage parameter on the leaf object and the parameterisation of NodeInitiationRate.  Your NodeInitiationRate is triggering a new leaf cohort before leaves have been initialised.");

            LeafCohort NewLeaf = InitialLeaves[0].Clone();
            NewLeaf.CohortPopulation = 0;
            NewLeaf.Age = 0;
            NewLeaf.Rank = InitParams.Rank;
            NewLeaf.Area = 0.0;
            NewLeaf.DoInitialisation();
            Leaves.Add(NewLeaf);
            needToRecalculateLiveDead = true;
        }

        /// <summary>Method to make leaf cohort appear and start expansion</summary>
        [EventSubscribe("LeafTipAppearance")]
        private void OnLeafTipAppearance(object sender, ApparingLeafParams CohortParams)
        {
            if (CohortsInitialised == false)
                throw new Exception("Trying to initialse new cohorts prior to InitialStage.  Check the InitialStage parameter on the leaf object and the parameterisation of NodeAppearanceRate.  Your NodeAppearanceRate is triggering a new leaf cohort before the initial leaves have been triggered.");
            if (CohortParams.CohortToAppear > InitialisedCohortNo)
                throw new Exception("MainStemNodeNumber exceeds the number of leaf cohorts initialised.  Check primordia parameters to make sure primordia are being initiated fast enough and for long enough");
            int i = CohortParams.CohortToAppear - 1;

            Leaves[i].DoAppearance(CohortParams, CohortParameters);
            needToRecalculateLiveDead = true;
            if (NewLeaf != null)
                NewLeaf.Invoke();
        }

        /// <summary>Does the nutrient allocations.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoActualPlantGrowth")]
        private void OnDoActualPlantGrowth(object sender, EventArgs e)
        {
            if (parentPlant.IsAlive)
            {
                double thermalTime = ThermalTime.Value();
                foreach (LeafCohort L in Leaves)
                {
                    L.DoActualGrowth(thermalTime, CohortParameters);
                    needToRecalculateLiveDead = true;
                }

                Structure.UpdateHeight();

                //Work out what proportion of the canopy has died today.  This variable is addressed by other classes that need to perform senescence proces at the same rate as leaf senescnce
                FractionDied = 0;
                if (DeadCohortNo > 0 && GreenCohortNo > 0)
                {
                    double deltaDeadLeaves = DeadCohortNo - DeadNodesYesterday; //Fixme.  DeadNodesYesterday is never given a value as far as I can see.
                    FractionDied = deltaDeadLeaves / GreenCohortNo;
                    DeadNodesYesterday = DeadCohortNo;
                }
            }
        }
        /// <summary>Zeroes the leaves.</summary>
        public virtual void ZeroLeaves()
        {
            Structure.LeafTipsAppeared = 0;
            Structure.Clear();
            Leaves.Clear();
            needToRecalculateLiveDead = true;
            Summary.WriteMessage(this, "Removing leaves from plant");
        }

        /// <summary>Fractional interception "above" a given node position</summary>
        /// <param name="cohortno">cohort position</param>
        /// <returns>fractional interception (0-1)</returns>
        public double CoverAboveCohort(double cohortno)
        {
            int MM2ToM2 = 1000000; // Conversion of mm2 to m2
            double LAIabove = 0;
            for (int i = Leaves.Count - 1; i > cohortno - 1; i--)
                LAIabove += Leaves[i].LiveArea / MM2ToM2;
            return 1 - Math.Exp(-ExtinctionCoeff.Value() * LAIabove);
        }

        /// <summary>
        /// remove biomass from the leaf.
        /// </summary>
        /// <param name="biomassRemoveType">Name of event that triggered this biomass remove call.</param>
        /// <param name="amountToRemove">The frations of biomass to remove</param>
        public void RemoveBiomass(string biomassRemoveType, OrganBiomassRemovalType amountToRemove)
        {
            bool writeToSummary = true;
            foreach (LeafCohort leaf in Leaves)
            {
                if (leaf.IsInitialised)
                {
                    double remainingLiveFraction = biomassRemovalModel.RemoveBiomass(biomassRemoveType, amountToRemove, leaf.Live, leaf.Dead, leaf.Removed, leaf.Detached, writeToSummary);
                    leaf.LiveArea *= remainingLiveFraction;
                    //writeToSummary = false; // only want it written once.
                    Detached.Add(leaf.Detached);
                    Removed.Add(leaf.Removed);
                }

                needToRecalculateLiveDead = true;
            }
        }

        /// <summary>
        /// remove population elements from the leaf.
        /// </summary>
        /// <param name="ProportionRemoved">The proportion of stems removed by thinning</param>
        public void DoThin(double ProportionRemoved)
        {
            foreach (LeafCohort leaf in Leaves)
                leaf.CohortPopulation *= 1 - ProportionRemoved;
        }

        /// <summary>
        /// Called when defoliation calls for removal of main-stem nodes
        /// </summary>
        public void RemoveHighestLeaf()
        {
            Leaves.RemoveAt(InitialisedCohortNo-1);
            needToRecalculateLiveDead = true;
            
            
        }
        #endregion

        #region Arbitrator methods

        /// <summary>Calculate and return the dry matter supply (g/m2)</summary>
        [EventSubscribe("SetDMSupply")]
        private void SetDMSupply(object sender, EventArgs e)
        {
            // Daily photosynthetic "net" supply of dry matter for the whole plant (g DM/m2/day)
            double Retranslocation = 0;
            double Reallocation = 0;

            foreach (LeafCohort L in Leaves)
            {
                Retranslocation += L.LeafStartDMRetranslocationSupply;
                Reallocation += L.LeafStartDMReallocationSupply;
            }

            DMSupply.Fixation = Photosynthesis.Value();
            DMSupply.Retranslocation = Retranslocation;
            DMSupply.Reallocation = Reallocation;
        }

        /// <summary>Calculate and return the nitrogen supply (g/m2)</summary>
        [EventSubscribe("SetNSupply")]
        private void SetNSupply(object sender, EventArgs e)
        {
            double RetransSupply = 0;
            double ReallocationSupply = 0;
            foreach (LeafCohort L in Leaves)
            {
                RetransSupply += Math.Max(0, L.LeafStartNRetranslocationSupply);
                ReallocationSupply += L.LeafStartNReallocationSupply;
            }
            NSupply.Retranslocation = RetransSupply;
            NSupply.Reallocation = ReallocationSupply;
        }

        /// <summary>Calculate and return the dry matter demand (g/m2)</summary>
        [EventSubscribe("SetDMDemand")]
        private void SetDMDemand(object sender, EventArgs e)
        {
            double StructuralDemand = 0.0;
            double StorageDemand = 0.0;
            double MetabolicDemand = 0.0;

            if (DMDemandFunction != null)
            {
                StructuralDemand = DMDemandFunction.Value() * StructuralFraction.Value();
                StorageDemand = DMDemandFunction.Value() * (1 - StructuralFraction.Value());
            }
            else
            {
                double dMConversionEfficiency = DMConversionEfficiency.Value();
                foreach (LeafCohort L in Leaves)
                {
                    StructuralDemand += L.StructuralDMDemand / dMConversionEfficiency;
                    MetabolicDemand += L.MetabolicDMDemand / dMConversionEfficiency;
                    StorageDemand += L.StorageDMDemand / dMConversionEfficiency;
                }
            }
            DMDemand.Structural = StructuralDemand;
            DMDemand.Metabolic = MetabolicDemand;
            DMDemand.Storage = StorageDemand;
        }

        /// <summary>Calculate and return the nitrogen demand (g/m2)</summary>
        [EventSubscribe("SetNDemand")]
        private void SetNDemand(object sender, EventArgs e)
        {
            double StructuralDemand = 0.0;
            double MetabolicDemand = 0.0;
            double StorageDemand = 0.0;
            foreach (LeafCohort L in Leaves)
            {
                StructuralDemand += L.StructuralNDemand;
                MetabolicDemand += L.MetabolicNDemand;
                StorageDemand += L.StorageNDemand;
            }
            NDemand.Structural = StructuralDemand;
            NDemand.Metabolic = MetabolicDemand;
            NDemand.Storage = StorageDemand;
        }

        /// <summary>Sets the dry matter potential allocation.</summary>
        public void SetDryMatterPotentialAllocation(BiomassPoolType dryMatter)
        {
            //Allocate Potential Structural DM
            if (DMDemand.Structural == 0 && dryMatter.Structural > 0.000000000001)
                throw new Exception("Invalid allocation of potential DM in" + Name);

            double[] CohortPotentialStructualDMAllocation = new double[Leaves.Count + 2];

            if (dryMatter.Structural != 0.0)
            {
                double DMPotentialsupply = dryMatter.Structural * DMConversionEfficiency.Value();
                double DMPotentialallocated = 0;
                double TotalPotentialDemand = Leaves.Sum(l => l.StructuralDMDemand);
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double PotentialAllocation = Math.Min(L.StructuralDMDemand,
                        DMPotentialsupply * (L.StructuralDMDemand / TotalPotentialDemand));
                    CohortPotentialStructualDMAllocation[i] = PotentialAllocation;
                    DMPotentialallocated += PotentialAllocation;
                }
                if (DMPotentialallocated - dryMatter.Structural > 0.000000001)
                    throw new Exception("the sum of poteitial DM allocation to leaf cohorts is more that that allocated to leaf organ");
            }

            //Allocate Metabolic DM
            if (DMDemand.Metabolic == 0 && dryMatter.Metabolic > 0.000000000001)
                throw new Exception("Invalid allocation of potential DM in" + Name);

            double[] CohortPotentialMetabolicDMAllocation = new double[Leaves.Count + 2];

            if (dryMatter.Metabolic != 0.0)
            {
                double DMPotentialsupply = dryMatter.Metabolic * DMConversionEfficiency.Value();
                double DMPotentialallocated = 0;
                double TotalPotentialDemand = Leaves.Sum(l => l.MetabolicDMDemand);
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double PotentialAllocation = Math.Min(L.MetabolicDMDemand,
                        DMPotentialsupply * L.MetabolicDMDemand / TotalPotentialDemand);
                    CohortPotentialMetabolicDMAllocation[i] = PotentialAllocation;
                    DMPotentialallocated += PotentialAllocation;
                }
                if (DMPotentialallocated - dryMatter.Metabolic > 0.000000001)
                    throw new Exception(
                        "the sum of poteitial DM allocation to leaf cohorts is more that that allocated to leaf organ");
            }

            //Send allocations to cohorts
            int a = 0;
            foreach (LeafCohort L in Leaves)
            {
                a++;
                L.DMPotentialAllocation = new BiomassPoolType
                {
                    Structural = CohortPotentialStructualDMAllocation[a],
                    Metabolic = CohortPotentialMetabolicDMAllocation[a],
                };
            }
        }

        /// <summary>Sets the dry matter allocation.</summary>
        public void SetDryMatterAllocation(BiomassAllocationType value)
        {
            // get DM lost by respiration (growth respiration)
            // GrowthRespiration with unit CO2 
            // GrowthRespiration is calculated as 
            // Allocated CH2O from photosynthesis "1 / DMConversionEfficiency.Value()", converted 
            // into carbon through (12 / 30), then minus the carbon in the biomass, finally converted into 
            // CO2 (44/12).
            double growthRespFactor = ((1 / DMConversionEfficiency.Value()) * (12.0 / 30.0) - 1 * CarbonConcentration.Value()) * 44.0 / 12.0;
            GrowthRespiration = (value.Structural + value.Storage + value.Metabolic) * growthRespFactor;
            
            double[] StructuralDMAllocationCohort = new double[Leaves.Count + 2];
            double StartWt = Live.StructuralWt + Live.MetabolicWt + Live.StorageWt;
            //Structural DM allocation
            if (DMDemand.Structural <= 0 && value.Structural > 0.000000000001)
                throw new Exception("Invalid allocation of DM in Leaf");
            if (value.Structural > 0.0)
            {
                double DMsupply = value.Structural * DMConversionEfficiency.Value();
                double DMallocated = 0;
                double TotalDemand = Leaves.Sum(l => l.StructuralDMDemand);
                double DemandFraction = value.Structural * DMConversionEfficiency.Value() / TotalDemand;
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double Allocation = Math.Min(L.StructuralDMDemand * DemandFraction, DMsupply);
                    StructuralDMAllocationCohort[i] = Allocation;
                    DMallocated += Allocation;
                    Allocated.StructuralWt += Allocation;
                    DMsupply -= Allocation;
                }
                if (DMsupply > 0.0000000001)
                    throw new Exception("DM allocated to Leaf left over after allocation to leaf cohorts");
                if (DMallocated - value.Structural * DMConversionEfficiency.Value() > 0.000000001)
                    throw new Exception("the sum of DM allocation to leaf cohorts is more than that available to leaf organ");
            }

            //Metabolic DM allocation
            double[] MetabolicDMAllocationCohort = new double[Leaves.Count + 2];

            if (DMDemand.Metabolic <= 0 && value.Metabolic > 0.000000000001)
                throw new Exception("Invalid allocation of DM in Leaf");
            if (value.Metabolic > 0.0)
            {
                double DMsupply = value.Metabolic * DMConversionEfficiency.Value();
                double DMallocated = 0;
                double TotalDemand = Leaves.Sum(l => l.MetabolicDMDemand);
                double DemandFraction = value.Metabolic * DMConversionEfficiency.Value() / TotalDemand;
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double Allocation = Math.Min(L.MetabolicDMDemand * DemandFraction, DMsupply);
                    MetabolicDMAllocationCohort[i] = Allocation;
                    DMallocated += Allocation;
                    Allocated.MetabolicWt += Allocation;
                    DMsupply -= Allocation;
                }
                if (DMsupply > 0.0000000001)
                    throw new Exception("Metabolic DM allocated to Leaf left over after allocation to leaf cohorts");
                if (DMallocated - value.Metabolic * DMConversionEfficiency.Value() > 0.000000001)
                    throw new Exception("the sum of Metabolic DM allocation to leaf cohorts is more that that allocated to leaf organ");
            }

            // excess allocation
            double[] StorageDMAllocationCohort = new double[Leaves.Count + 2];
            double TotalSinkCapacity = 0;
            foreach (LeafCohort L in Leaves)
                TotalSinkCapacity += L.StorageDMDemand;
            if (value.Storage * DMConversionEfficiency.Value() > TotalSinkCapacity)
            //Fixme, this exception needs to be turned on again
            { }
            //throw new Exception("Allocating more excess DM to Leaves then they are capable of storing");
            if (TotalSinkCapacity > 0.0)
            {
                double SinkFraction = (value.Storage * DMConversionEfficiency.Value()) / TotalSinkCapacity;
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double allocation = Math.Min(L.StorageDMDemand * SinkFraction, value.Storage * DMConversionEfficiency.Value());
                    StorageDMAllocationCohort[i] = allocation;
                }
            }

            // retranslocation
            double[] DMRetranslocationCohort = new double[Leaves.Count + 2];

            if (value.Retranslocation - DMSupply.Retranslocation > 0.0000000001)
                throw new Exception(Name + " cannot supply that amount for DM retranslocation");
            if (value.Retranslocation > 0)
            {
                double remainder = value.Retranslocation;
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double Supply = Math.Min(remainder, L.LeafStartDMRetranslocationSupply);
                    DMRetranslocationCohort[i] = Supply;
                    remainder -= Supply;
                }
                if (remainder > 0.0000000001)
                    throw new Exception(Name + " DM Retranslocation demand left over after processing.");
            }

            // Reallocation
            double[] DMReAllocationCohort = new double[Leaves.Count + 2];
            if (value.Reallocation - DMSupply.Reallocation > 0.000000001)
                throw new Exception(Name + " cannot supply that amount for DM Reallocation");
            if (value.Reallocation < -0.000000001)
                throw new Exception(Name + " recieved -ve DM reallocation");
            if (value.Reallocation > 0)
            {
                double remainder = value.Reallocation;
                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double ReAlloc = Math.Min(remainder, L.LeafStartDMReallocationSupply);
                    remainder = Math.Max(0.0, remainder - ReAlloc);
                    DMReAllocationCohort[i] = ReAlloc;
                }
                if (!MathUtilities.FloatsAreEqual(remainder, 0.0))
                   throw new Exception(Name + " DM Reallocation demand left over after processing.");

            }

            //Send allocations to cohorts
            int a = 0;
            foreach (LeafCohort L in Leaves)
            {
                a++;
                L.DMAllocation = new BiomassAllocationType
                {
                    Structural = StructuralDMAllocationCohort[a],
                    Metabolic = MetabolicDMAllocationCohort[a],
                    Storage = StorageDMAllocationCohort[a],
                    Retranslocation = DMRetranslocationCohort[a],
                    Reallocation = DMReAllocationCohort[a],
                };
                needToRecalculateLiveDead = true;
            }

            double EndWt = Live.StructuralWt + Live.MetabolicWt + Live.StorageWt;
            double CheckValue = StartWt + (value.Structural + value.Metabolic + value.Storage) * DMConversionEfficiency.Value() - value.Reallocation - value.Retranslocation - value.Respired;
            double ExtentOfError = Math.Abs(EndWt - CheckValue);
            double FloatingPointError = 0.00000001;
            if (ExtentOfError > FloatingPointError)
                throw new Exception(Name + ": " + ExtentOfError.ToString() + " of DM allocation was not used");
        }

        /// <summary>Sets the n allocation.</summary>
        public void SetNitrogenAllocation(BiomassAllocationType nitrogen)
        {
            if (NDemand.Structural == 0 && nitrogen.Structural > 0) //FIXME this needs to be seperated into compoents
                throw new Exception("Invalid allocation of N");

            double StartN = Live.StructuralN + Live.MetabolicN + Live.StorageN;

            double[] StructuralNAllocationCohort = new double[Leaves.Count + 2];
            double[] MetabolicNAllocationCohort = new double[Leaves.Count + 2];
            double[] StorageNAllocationCohort = new double[Leaves.Count + 2];
            double[] NReallocationCohort = new double[Leaves.Count + 2];
            double[] NRetranslocationCohort = new double[Leaves.Count + 2];

            if (nitrogen.Structural + nitrogen.Metabolic + nitrogen.Storage > 0.0)
            {
                //setup allocation variables
                double[] CohortNAllocation = new double[Leaves.Count + 2];
                double[] StructuralNDemand = new double[Leaves.Count + 2];
                double[] MetabolicNDemand = new double[Leaves.Count + 2];
                double[] StorageNDemand = new double[Leaves.Count + 2];
                double TotalStructuralNDemand = 0;
                double TotalMetabolicNDemand = 0;
                double TotalStorageNDemand = 0;

                int i = 0;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    CohortNAllocation[i] = 0;
                    StructuralNDemand[i] = L.StructuralNDemand;
                    TotalStructuralNDemand += L.StructuralNDemand;
                    MetabolicNDemand[i] = L.MetabolicNDemand;
                    TotalMetabolicNDemand += L.MetabolicNDemand;
                    StorageNDemand[i] = L.StorageNDemand;
                    TotalStorageNDemand += L.StorageNDemand;
                }
                double NSupplyValue = nitrogen.Structural;

                // first make sure each cohort gets the structural N requirement for growth (includes MinNconc for structural growth and MinNconc for Storage growth)
                if (NSupplyValue > 0 && TotalStructuralNDemand > 0)
                {
                    i = 0;
                    foreach (LeafCohort L in Leaves)
                    {
                        i++;
                        StructuralNAllocationCohort[i] = Math.Min(StructuralNDemand[i], NSupplyValue * (StructuralNDemand[i] / TotalStructuralNDemand));
                    }

                }
                // then allocate additional N relative to leaves metabolic demands
                NSupplyValue = nitrogen.Metabolic;
                if (NSupplyValue > 0 && TotalMetabolicNDemand > 0)
                {
                    i = 0;
                    foreach (LeafCohort L in Leaves)
                    {
                        i++;
                        MetabolicNAllocationCohort[i] = Math.Min(MetabolicNDemand[i],
                            NSupplyValue * (MetabolicNDemand[i] / TotalMetabolicNDemand));
                    }
                }
                // then allocate excess N relative to leaves N sink capacity
                NSupplyValue = nitrogen.Storage;
                if (NSupplyValue > 0 && TotalStorageNDemand > 0)
                {
                    i = 0;
                    foreach (LeafCohort L in Leaves)
                    {
                        i++;
                        StorageNAllocationCohort[i] += Math.Min(StorageNDemand[i], NSupplyValue * (StorageNDemand[i] / TotalStorageNDemand));
                    }
                }
            }

            // Retranslocation
            if (nitrogen.Retranslocation - NSupply.Retranslocation > 0.000000001)
                throw new Exception(Name + " cannot supply that amount for N retranslocation");
            if (nitrogen.Retranslocation < -0.000000001)
                throw new Exception(Name + " recieved -ve N retranslocation");
            if (nitrogen.Retranslocation > 0)
            {
                int i = 0;
                double remainder = nitrogen.Retranslocation;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double Retrans = Math.Min(remainder, L.LeafStartNRetranslocationSupply);
                    NRetranslocationCohort[i] = Retrans;
                    remainder = Math.Max(0.0, remainder - Retrans);
                }
                if (!MathUtilities.FloatsAreEqual(remainder, 0.0))
                    throw new Exception(Name + " N Retranslocation demand left over after processing.");
            }

            // Reallocation
            if (nitrogen.Reallocation - NSupply.Reallocation > 0.000000001)
                throw new Exception(Name + " cannot supply that amount for N Reallocation");
            if (nitrogen.Reallocation < -0.000000001)
                throw new Exception(Name + " recieved -ve N reallocation");
            if (nitrogen.Reallocation > 0)
            {
                int i = 0;
                double remainder = nitrogen.Reallocation;
                foreach (LeafCohort L in Leaves)
                {
                    i++;
                    double ReAlloc = Math.Min(remainder, L.LeafStartNReallocationSupply);
                    NReallocationCohort[i] = ReAlloc;
                    remainder = Math.Max(0.0, remainder - ReAlloc);
                }
                if (!MathUtilities.FloatsAreEqual(remainder, 0.0))
                    throw new Exception(Name + " N Reallocation demand left over after processing.");
            }

            //Send allocations to cohorts
            int a = 0;
            foreach (LeafCohort L in Leaves)
            {
                a++;
                L.NAllocation = new BiomassAllocationType
                {
                    Structural = StructuralNAllocationCohort[a],
                    Metabolic = MetabolicNAllocationCohort[a],
                    Storage = StorageNAllocationCohort[a],
                    Retranslocation = NRetranslocationCohort[a],
                    Reallocation = NReallocationCohort[a],
                };
            }
            needToRecalculateLiveDead = true;

            double endN = Live.StructuralN + Live.MetabolicN + Live.StorageN;
            double checkValue = StartN + nitrogen.Structural + nitrogen.Metabolic + nitrogen.Storage -
                                nitrogen.Reallocation - nitrogen.Retranslocation - nitrogen.Respired;
            double extentOfError = Math.Abs(endN - checkValue);
            if (extentOfError > 0.00000001)
                throw new Exception(Name + "Some Leaf N was not allocated.");
        }

        /// <summary>Remove maintenance respiration from live component of organs.</summary>
        /// <param name="respiration">The respiration to remove</param>
        public virtual void RemoveMaintenanceRespiration(double respiration)
        {
            double totalResLeaf = MaintenanceRespiration;

            foreach (LeafCohort L in Leaves)
            {
                double totalBMLeafCohort = L.Live.MetabolicWt + L.Live.StorageWt;
                double resLeafCohort = respiration * L.MaintenanceRespiration / totalResLeaf;

                if (resLeafCohort > Double.Epsilon && resLeafCohort - totalBMLeafCohort > 0.00001)
                    throw new Exception("Respiration is more than total biomass of metabolic and storage in live component.");

                if (resLeafCohort > 0 && (L.Live.MetabolicWt + L.Live.StorageWt) > 0)
                {
                    L.Live.MetabolicWt -= (resLeafCohort * L.Live.MetabolicWt / totalBMLeafCohort);
                    L.Live.StorageWt -= (resLeafCohort * L.Live.StorageWt / totalBMLeafCohort);
                    needToRecalculateLiveDead = true;
                }
            }
        }


        /// <summary>Gets or sets the minimum nconc.</summary>
        public double MinNconc
        {
            get
            {
                return CohortParameters.CriticalNConc.Value();
            }
        }

        /// <summary>Gets the total biomass</summary>
        public Biomass Total { get { return Live + Dead; } }

        /// <summary>Gets the total grain weight</summary>
        [Units("g/m2")]
        public double Wt { get { return Total.Wt; } }

        /// <summary>Gets the total grain N</summary>
        [Units("g/m2")]
        public double N { get { return Total.N; } }

        #endregion

        #region Event handlers

        /// <summary>Occurs when [new leaf].</summary>
        public event NullTypeDelegate NewLeaf;

        /// <summary>Called when [remove lowest leaf].</summary>
        [EventSubscribe("RemoveLowestLeaf")]
        private void OnRemoveLowestLeaf()
        {
            Summary.WriteMessage(this, "Removing lowest Leaf");
            Leaves.RemoveAt(0);
            needToRecalculateLiveDead = true;
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        private void OnPlantSowing(object sender, SowPlant2Type data)
        {
            if (data.Plant == parentPlant)
            {
                Reset();
                if (data.MaxCover <= 0.0)
                    throw new Exception("MaxCover must exceed zero in a Sow event.");
                MaxCover = data.MaxCover;
            }
        }

        /// <summary>Called when [kill leaf].</summary>
        /// <param name="KillLeaf">The kill leaf.</param>
        [EventSubscribe("KillLeaf")]
        private void OnKillLeaf(KillLeafType KillLeaf)
        {
            Summary.WriteMessage(this, "Killing " + KillLeaf.KillFraction + " of leaves on plant");
            foreach (LeafCohort L in Leaves)
            {
                L.DoKill(KillLeaf.KillFraction);
                needToRecalculateLiveDead = true;
            }
        }


        /// <summary>Called when crop is being prunned.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Pruning")]
        private void OnPruning(object sender, EventArgs e)
        {
            Structure.CohortToInitialise = 0;
            Structure.TipToAppear =  0;
            Structure.Clear();
            Structure.ResetStemPopn();
            Structure.NextLeafProportion = 1.0;

            Leaves.Clear();
            needToRecalculateLiveDead = true;
            CohortsAtInitialisation = 0;
            TipsAtEmergence = 0;
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            DMDemand = new BiomassPoolType();
            NDemand = new BiomassPoolType();
            DMSupply = new BiomassSupplyType();
            NSupply = new BiomassSupplyType();
            Allocated = new Biomass();
            Senesced = new Biomass();
            Detached = new Biomass();
            Removed = new Biomass();
            List<LeafCohort> initialLeaves = new List<LeafCohort>();
            foreach (LeafCohort initialLeaf in Apsim.Children(this, typeof(LeafCohort)))
                initialLeaves.Add(initialLeaf);
            InitialLeaves = initialLeaves.ToArray();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        private void OnPlantEnding(object sender, EventArgs e)
        {
            if (Wt > 0.0)
            {
                Detached.Add(Live);
                Detached.Add(Dead);
                SurfaceOrganicMatter.Add(Wt * 10, N * 10, 0, parentPlant.CropType, Name);
            }

            Reset();
            CohortsAtInitialisation = 0;
        }

        /// <summary>Called when crop is being cut.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Harvesting")]
        private void OnHarvesting(object sender, EventArgs e)
        {
            CohortsAtInitialisation = 0;
        }

        /// <summary>Called when [do daily initialisation].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
         protected void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            if (parentPlant.IsAlive || parentPlant.IsEnding)
            {
                ClearBiomassFlows();
                foreach (LeafCohort leaf in Leaves)
                    leaf.DoDailyCleanup();
            }
        }

        /// <summary>Clears the transferring biomass amounts.</summary>
        private void ClearBiomassFlows()
        {
            Allocated.Clear();
            Senesced.Clear();
            Detached.Clear();
            Removed.Clear();
        }
        #endregion

    }
}