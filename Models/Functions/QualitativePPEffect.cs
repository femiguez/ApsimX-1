﻿using System;
using Models.Core;
using Models.Functions;
namespace Models.Functions
{

    ///<summary>
    /// # [Name]
    /// Qualitative Photoperiod effect on developmental rate
    ///</summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class QualitativePPEffect : Model, IFunction
    {
        /// <summary>The photoperiod</summary>
        [Link]
        IFunction Photoperiod = null;

        /// <summary>Gets or sets the Optimum Photoperiod</summary>
        /// <value>Optimum Photoperiod</value>
        [Description("Optimum Photoperiod for development")]
        public double OptimumPhotoperiod { get; set; }
        /// <summary>Gets or sets the Critical Photoperiod</summary>
        /// <value>Critical Photoperiod</value>
        [Description("Critical Photoperiod for development")]
        public double CriticalPhotoperiod { get; set; }

        /// <summary>Photoperiod factor</summary>
        public double Value(int arrayIndex = -1)
        {
            double PS = Math.Pow(Math.Abs(OptimumPhotoperiod - CriticalPhotoperiod), -2);

            double photop_eff;

            if (OptimumPhotoperiod > CriticalPhotoperiod && Photoperiod.Value() > OptimumPhotoperiod)
                photop_eff = 1;
            else if (OptimumPhotoperiod < CriticalPhotoperiod && Photoperiod.Value() < OptimumPhotoperiod)
                photop_eff = 1;
            else
                photop_eff = 1 - PS * Math.Pow(Math.Abs(OptimumPhotoperiod - Photoperiod.Value()), 2);

            photop_eff = Math.Max(photop_eff, 0.0);
            photop_eff = Math.Min(photop_eff, 1.0);

            return photop_eff;
        }
    }
}
