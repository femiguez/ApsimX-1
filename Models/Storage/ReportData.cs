﻿namespace Models.Storage
{
    using APSIM.Shared.Utilities;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Data;
    using System.Reflection;
    using System.Xml.Serialization;

    /// <summary>Encapsulates a table that needs writing to the database.</summary>
    [Serializable]
    public class ReportData
    {
        /// <summary>The name of the checkpoint the data belongs to.</summary>
        public string CheckpointName { get; set; }

        /// <summary>The name of the simulation the data belongs to.</summary>
        public string SimulationName { get; set; }

        /// <summary>The name of the folder the simulation belongs in.</summary>
        public string FolderName { get; set; }

        /// <summary>The name of the table to write the data to.</summary>
        public string TableName { get; set; }

        /// <summary>The table column names to write the data to.</summary>
        public IList<string> ColumnNames { get; set; }

        /// <summary>The units for each of the columns.</summary>
        public IList<string> ColumnUnits { get; set; }

        /// <summary>The rows of data to write.</summary>
        public List<List<object>> Rows { get; set; } = new List<List<object>>();

        /// <summary>Create and return a datatable containing all rows and columns.</summary>
        public DataTable ToTable()
        {
            // Setup a table.
            var table = new DataTable(TableName);

            // Loop through all rows and add the values to the table.
            foreach (var row in Rows)
            {
                // Add a new row.
                var newRow = table.NewRow();

                // Add in all values for this row.
                for (int columnIndex = 0; columnIndex < ColumnNames.Count; columnIndex++)
                    FlattenValueIntoRow(ColumnNames[columnIndex], ColumnUnits[columnIndex],
                                        row[columnIndex], newRow);

                table.Rows.Add(newRow);
            }

            return table;
        }

        /// <summary>
        /// 'Flatten' a value (if it is an array or structure) into something that can be
        /// stored in a flat database table.
        /// </summary>
        /// <param name="name">The column name.</param>
        /// <param name="units">The units of the value.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="row">Row to store value into to.</param>
        private void FlattenValueIntoRow(string name, string units, object value, DataRow row)
        {
            if (value == null)
                AddColumnToTable(row.Table, name);
            else if (value.GetType() == typeof(DateTime) || value.GetType() == typeof(string) || !value.GetType().IsClass)
            {
                // Scalar
                var newColumnName = AddColumnToTable(row.Table, name, value.GetType());
                row[newColumnName] = value;
            }
            else if (value.GetType().GetInterface("IList") != null)
            {
                // List
                IList array = value as IList;
                for (int arrayIndex = 0; arrayIndex < array.Count; arrayIndex++)
                {
                    int startIndex = 0;
                    string heading = name;
                    string arraySpecification = StringUtilities.SplitOffBracketedValue(ref heading, '(', ')');
                    if (arraySpecification != string.Empty)
                        startIndex = Convert.ToInt32(arraySpecification) - 1;

                    heading += "(" + (startIndex + arrayIndex + 1).ToString() + ")";

                    object arrayElement = array[arrayIndex];
                    FlattenValueIntoRow(heading, units, arrayElement, row);  // recursion                }
                }
            }
            else
            {
                // A struct or class
                foreach (PropertyInfo property in ReflectionUtilities.GetPropertiesSorted(value.GetType(), BindingFlags.Instance | BindingFlags.Public))
                {
                    object[] attrs = property.GetCustomAttributes(true);
                    string propUnits = null;
                    bool ignore = false;
                    foreach (object attr in attrs)
                    {
                        if (attr is XmlIgnoreAttribute)
                        {
                            ignore = true;
                            continue;
                        }
                        Core.UnitsAttribute unitsAttr = attr as Core.UnitsAttribute;
                        if (unitsAttr != null)
                            propUnits = unitsAttr.ToString();
                    }
                    if (ignore)
                        continue;
                    string heading = name + "." + property.Name;
                    object classElement = property.GetValue(value, null);
                    FlattenValueIntoRow(heading, propUnits, classElement, row);  // recursion
                }
            }
        }

        /// <summary>
        /// Add a new column to the table if it doesn't exist.
        /// </summary>
        /// <param name="table">The table to add the column to.</param>
        /// <param name="columnName">The name of the column.</param>
        /// <param name="dataType">The data type of the column.</param>
        /// <returns>The new column name.</returns>
        private string AddColumnToTable(DataTable table, string columnName, Type dataType = null)
        {
            if (!table.Columns.Contains(columnName))
            {
                if (dataType == null)
                    table.Columns.Add(columnName);
                else
                    table.Columns.Add(columnName, dataType);
            }
            return columnName;
        }
    }
}