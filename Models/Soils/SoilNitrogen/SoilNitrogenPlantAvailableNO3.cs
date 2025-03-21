﻿namespace Models.Soils
{
    using Interfaces;
    using System;
    using Models.Core;
    using System.Xml.Serialization;

    /// <summary>This class encapsulates a SoilNitrogen model 'PlantAvailableNO3' solute.</summary>
    [Serializable]
    public class SoilNitrogenPlantAvailableNO3 : Model, ISolute
    {
        [ParentLink]
        SoilNitrogen parent = null;

        /// <summary>Solute amount (kg/ha)</summary>
        [XmlIgnore]
        public double[] kgha
        {
            get
            {
                return parent.CalculatePlantAvailableNO3();
            }
            set
            {
                SetKgHa(SoluteSetterType.Plant, value);
            }
        }

        /// <summary>Solute amount (ppm)</summary>
        public double[] ppm { get { return parent.Soil.kgha2ppm(kgha); } }

        /// <summary>Setter for kgha.</summary>
        /// <param name="callingModelType">Type of calling model.</param>
        /// <param name="value">New values.</param>
        public void SetKgHa(SoluteSetterType callingModelType, double[] value)
        {
            parent.SetPlantAvailableNO3(callingModelType, value);
        }

        /// <summary>Setter for kgha delta.</summary>
        /// <param name="callingModelType">Type of calling model</param>
        /// <param name="delta">New delta values</param>
        public void AddKgHaDelta(SoluteSetterType callingModelType, double[] delta)
        {
            throw new NotImplementedException("should not be trying to set plant available no3");
        }
    }
}
