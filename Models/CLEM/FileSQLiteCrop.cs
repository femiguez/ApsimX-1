using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Models.Core;
using APSIM.Shared.Utilities;
using Models.Interfaces;
using System.ComponentModel.DataAnnotations;
using Models.Core.Attributes;
using Models.CLEM.Activities;
using System.Globalization;

// -----------------------------------------------------------------------
// <copyright file="FileCrop.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
//-----------------------------------------------------------------------
namespace Models.CLEM
{
    ///<summary>
    /// Reads in crop growth data from an APSIM SQLite database and makes it available to other models.
    /// Required columns are (you can provide a link to each column name):
    /// Year
    /// Month
    /// Soil id
    /// Crop name
    /// Amount harvested
    /// Percent nitrogen (optional)
    ///</summary>
    ///<remarks>
    ///</remarks>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")] 
    [PresenterName("UserInterface.Presenters.PropertyPresenter")] 
    [ValidParent(ParentType = typeof(ZoneCLEM))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("This model holds a crop data file from a APSIM SQLite database for the CLEM simulation.")]
    [Version(1, 0, 1, "")]
    [Version(1, 0, 2, "Added ability to define table and columns to use")]
    [HelpUri(@"Content/Features/DataReaders/CropDataReaderSQLite.htm")]
    public class FileSQLiteCrop : CLEMModel, IFileCrop
    {
        private bool nitrogenColumnExists = false;

        /// <summary>
        /// Gets or sets the file name. Should be relative filename where possible.
        /// </summary>
        [Summary]
        [Description("Crop database file name")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Crop database file name must be supplied")]
        public string FileName { get; set; }

        /// <summary>
        /// Defines the name of the table in the database holding the crop data.
        /// </summary>
        [Summary]
        [Description("Database table name")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Database table name must be supplied")]
        public string TableName { get; set; }

        /// <summary>
        /// Name of column holding crop name data
        /// </summary>
        [Summary]
        [System.ComponentModel.DefaultValueAttribute("CropName")]
        [Description("Column name for crop name")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Crop name column name must be supplied")]
        public string CropNameColumnName { get; set; }

        /// <summary>
        /// Name of column holding soil type data
        /// </summary>
        [Summary]
        [System.ComponentModel.DefaultValueAttribute("SoilNum")]
        [Description("Column name for land id")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Land id column name must be supplied")]
        public string SoilTypeColumnName { get; set; }

        /// <summary>
        /// Name of column holding year data
        /// </summary>
        [Summary]
        [System.ComponentModel.DefaultValueAttribute("Year")]
        [Description("Column name for year")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Year column name must be supplied")]
        public string YearColumnName { get; set; }

        /// <summary>
        /// Name of column holding month data
        /// </summary>
        [Summary]
        [System.ComponentModel.DefaultValueAttribute("Month")]
        [Description("Column name for month")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Month column name must be supplied")]
        public string MonthColumnName { get; set; }

        /// <summary>
        /// Name of column holding amount data
        /// </summary>
        [Summary]
        [System.ComponentModel.DefaultValueAttribute("AmtKg")]
        [Description("Column name for amount")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Amount column name must be supplied")]
        public string AmountColumnName { get; set; }

        /// <summary>
        /// Name of column holding nitrogen data
        /// </summary>
        [Summary]
        [Description("Column name for percent nitrogen")]
        public string PercentNitrogenColumnName { get; set; }

        /// <summary>
        /// Gets or sets the full file name (with path). 
        /// The Commands.ChangeProperty() uses this property to change the model.
        /// This is done after the user changes the file using the browse button in the View.
        /// </summary>
        [XmlIgnore]
        public string FullFileName
        {
            get
            {
                if ((this.FileName == null) || (this.FileName  == ""))
                {
                    return "";
                }  
                else
                {
                    Simulation simulation = Apsim.Parent(this, typeof(Simulation)) as Simulation;
                    if (simulation != null)
                    {
                        return PathUtilities.GetAbsolutePath(this.FileName, simulation.FileName);
                    }
                    else
                    {
                        return this.FileName;
                    }
                }
            }
        }

        /// <summary>
        /// Does file exist
        /// </summary>
        public bool FileExists
        {
            get { return File.Exists(this.FullFileName); }
        }

        /// <summary>
        /// Overrides the base class method to allow for initialization.
        /// </summary>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            SQLite sQLiteReader = new SQLite();
            try
            {
                sQLiteReader.OpenDatabase(FullFileName, true);

                // check table exists
                if(!sQLiteReader.GetTableNames().Contains(TableName))
                {
                    string errorMsg = "The specified table named ["+TableName+ "] was not found\n. Please not these table names are case sensitive.";
                    throw new ApsimXException(this, errorMsg);
                }

                Dictionary<string, string> columnLinks = new Dictionary<string, string>()
                {
                    { "year", YearColumnName },
                    { "month", MonthColumnName },
                    { "soil", SoilTypeColumnName },
                    { "crop", CropNameColumnName },
                    { "amount", AmountColumnName },
                    { "N", PercentNitrogenColumnName }
                };
                foreach (var item in columnLinks)
                {
                    // check each column name exists
                    if (!(item.Key == "N" & item.Value == ""))
                    {
                        if (!sQLiteReader.GetColumnNames(TableName).Contains(item.Value))
                        {
                            string errorMsg = "The specified column [" + item.Key + "] does not exist in the table named [" + TableName + "]\nEnsure the column name is present in the table. Please not these column names are case sensitive.";
                            throw new ApsimXException(this, errorMsg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = "@error:There was a problem with the SQLite database [o=" + FileName + "] for [x=" + this.Name + "]\n" + ex.Message;
                throw new ApsimXException(this, errorMsg);
            }
            if(sQLiteReader.IsOpen)
            {
                sQLiteReader.CloseDatabase();
            }
        }

        /// <summary>
        /// Provides an error message to display if something is wrong.
        /// Used by the UserInterface to give a warning of what is wrong
        /// When the user selects a file using the browse button in the UserInterface 
        /// and the file can not be displayed for some reason in the UserInterface.
        /// </summary>
        [XmlIgnore]
        public string ErrorMessage = string.Empty;

        /// <summary>
        /// Constructor
        /// </summary>
        public FileSQLiteCrop()
        {
            base.SetDefaults();
            base.ModelSummaryStyle = HTMLSummaryStyle.FileReader;
        }

        /// <summary>
        /// Searches the DataTable created from the Forage File using the specified parameters.
        /// <returns></returns>
        /// </summary>
        /// <param name="soilId">Name of soil or run name in database</param>
        /// <param name="cropName">Name of crop in database</param>
        /// <param name="startDate">Start date of the simulation</param>
        /// <param name="endDate">End date of the simulation</param>
        /// <returns>A struct called CropDataType containing the crop data for this month.
        /// This struct can be null. 
        /// </returns>
        public List<CropDataType> GetCropDataForEntireRun(string soilId, string cropName,
                                        DateTime startDate, DateTime endDate)
        {
            // check SQL file
            SQLite sQLiteReader = new SQLite();
            try
            {
                sQLiteReader.OpenDatabase(FullFileName, true);
            }
            catch (Exception ex)
            {
                ErrorMessage = "@error:There was a problem opening the SQLite database [o=" + FullFileName + "for [x=" + this.Name + "]\n" + ex.Message;
            }

            // check if Npct column exists in database
            nitrogenColumnExists = sQLiteReader.GetColumnNames(TableName).Contains(PercentNitrogenColumnName);

            // define SQL filter to load data
            string sqlQuery = "SELECT " + YearColumnName + "," + MonthColumnName + "," + CropNameColumnName + "," + SoilTypeColumnName + "," + AmountColumnName + "" + (nitrogenColumnExists ? "," + PercentNitrogenColumnName : "") + " FROM " + TableName
                + " WHERE " + SoilTypeColumnName + " = '" + soilId + "'"
                + " AND " + CropNameColumnName + " = '" + cropName + "'";

            if (startDate.Year == endDate.Year)
            {
                sqlQuery += " AND (( "+YearColumnName+" = " + startDate.Year + " AND " + MonthColumnName + " >= " + startDate.Month + " AND " + MonthColumnName + " < " + endDate.Month + ")"
                + ")";
            }
            else
            {
                sqlQuery += " AND (( " + YearColumnName + " = " + startDate.Year + " AND " + MonthColumnName + " >= " + startDate.Month + ")"
                + " OR  ( " + YearColumnName + " > " + startDate.Year + " AND " + YearColumnName + " < " + endDate.Year + ")"
                + " OR  ( " + YearColumnName + " = " + endDate.Year + " AND " + MonthColumnName + " < " + endDate.Month + ")"
                + ")";
            }

            DataTable results;
            try
            {
                results = sQLiteReader.ExecuteQuery(sqlQuery);
            }
            catch(Exception ex)
            {
                string errorMsg = "@error:There was a problem accessing the SQLite database [o=" + FullFileName + "] for [x=" + this.Name + "]\n" + ex.Message;
                throw new ApsimXException(this, errorMsg);
            }

            if (sQLiteReader.IsOpen)
            {
                sQLiteReader.CloseDatabase();
            }

            List<CropDataType> cropDetails = new List<CropDataType>();
            if (results.Rows.Count > 0)
            {
                results.DefaultView.Sort = YearColumnName+","+MonthColumnName;

                // convert to list<CropDataType>
                foreach (DataRowView row in results.DefaultView)
                {
                    cropDetails.Add(DataRow2CropData(row.Row));
                }
            }

            return cropDetails;
        }

        private CropDataType DataRow2CropData(DataRow dr)
        {
            CropDataType cropdata = new CropDataType
            {
                SoilNum = dr[SoilTypeColumnName].ToString(),
                CropName = dr[CropNameColumnName].ToString(),
                Year = int.Parse(dr[YearColumnName].ToString()),
                Month = int.Parse(dr[MonthColumnName].ToString()),
                AmtKg = double.Parse(dr[AmountColumnName].ToString())
            };
            if(nitrogenColumnExists)
            {
                cropdata.Npct = double.Parse(dr[PercentNitrogenColumnName].ToString(), CultureInfo.InvariantCulture);
            }
            else
            {
                cropdata.Npct = double.NaN;
            }
            cropdata.HarvestDate = new DateTime(cropdata.Year, cropdata.Month, 1);

            return cropdata;
        }

        /// <summary>
        /// Provides the description of the model settings for summary (GetFullSummary)
        /// </summary>
        /// <param name="formatForParentControl">Use full verbose description</param>
        /// <returns></returns>
        public override string ModelSummary(bool formatForParentControl)
        {
            string html = "";
            html += "\n<div class=\"activityentry\">";
            if (FileName == null || FileName == "")
            {
                html += "Using <span class=\"errorlink\">[FILE NOT SET]</span>";
                html += "\n</div>";
            }
            else if (!this.FileExists)
            {
                html += "The file <span class=\"errorlink\">" + FullFileName + "</span> could not be found";
                html += "\n</div>";
            }
            else
            {
                html += "Using <span class=\"filelink\">" + FileName + "</span>";

                if (TableName == null || TableName == "")
                {
                    html += "\n<div class=\"activityentry\" style=\"Margin-left:15px;\">Using table <span class=\"errorlink\">[TABLE NOT SET]</span></div>";
                }
                else
                {
                    // Add table name
                    html += "\n<div class=\"activityentry\" style=\"Margin-left:15px;\">";
                    html += "Using table <span class=\"filelink\">" + TableName + "</span>";
                    // add column links
                    html += "\n<div class=\"activityentry\" style=\"Margin-left:15px;\">";
                    html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Land id</span> is <span class=\"setvalue\">" + SoilTypeColumnName + "</span></div>";
                    html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Crop name</span> is <span class=\"setvalue\">" + CropNameColumnName + "</span></div>";
                    html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Year</span> is <span class=\"setvalue\">" + YearColumnName + "</span></div>";
                    html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Month</span> is <span class=\"setvalue\">" + MonthColumnName + "</span></div>";
                    html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Growth</span> is <span class=\"setvalue\">" + AmountColumnName + "</span></div>";
                    if(PercentNitrogenColumnName == "")
                    {
                        html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Nitrogen</span> is <span class=\"setvalue\">IGNORED</span></div>";
                    }
                    else
                    {
                        html += "\n<div class=\"activityentry\">Column name for <span class=\"filelink\">Nitrogen</span> is <span class=\"setvalue\">" + PercentNitrogenColumnName + "</span></div>";
                    }
                    html += "\n</div>";
                }
                html += "\n</div>";
            }
            return html;

        }
    }

}
