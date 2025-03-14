﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Models.CLEM
{
    /// <summary>
    /// A class to hold and question the existence of warnings generated by resources or activities
    /// Allows to track whether a particular warning has previously occurred for avoiding multiple error display etc.
    /// </summary>
    [Serializable]
    public class WarningLog
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public WarningLog()
        {
            warningList = new List<string>();
        }

        private List<string> warningList { get; set; }

        /// <summary>
        /// Add new warning to the ists
        /// </summary>
        /// <param name="name">Name of warning</param>
        public void Add(string name)
        {
            if(!warningList.Contains(name))
            {
                warningList.Add(name.ToUpper());
            }
        }

        /// <summary>
        /// Determine if warning exists
        /// </summary>
        /// <param name="name">name of warning</param>
        /// <returns></returns>
        public bool Exists(string name)
        {
            return warningList.Contains(name.ToUpper());
        }
    }
}
