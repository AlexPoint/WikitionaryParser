﻿using ALE.ETLBox;
using ALE.ETLBox.ConnectionManager;
using ALE.ETLBox.ControlFlow;
using ALE.ETLBox.DataFlow;
using ALE.ETLBox.Logging;
using ETL.Src;
using ETL.Src.Query;
using ETL.Src.Transform;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.FileExtensions;
using Microsoft.Extensions.Configuration.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL
{
    class Program
    {
        static void Main(string[] args)
        {
            //ForbesDataEtl();

            WikiDumpsEtl();
        }


        // Wiki dumps ETL -------------------------------------------------------------------------------------------------------

        public static void WikiDumpsEtl()
        {
            var builder = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json");
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();
            var connectionString = config["connectionString"];

            //var timestamp = DateTime.Now.ToString("yyyyMMdd");
            //var dbName = string.Format("tucdb_{0}", timestamp);
            var dbName = "tucdb";

            var csvFilePath = "C:/Users/Alex/Documents/Github/WikimediaParser/Test/Results/wiki-dumps-infoboxes-1.csv";

            /*WikiCsvToRaw(connectionString, dbName, csvFilePath, "dbo.WikiInfoboxPropertiesRaw");
            Console.WriteLine("=======================");
            // Add some cleaning steps for markdown here
            CleanInfoboxPropertyNames(connectionString, dbName, "dbo.WikiInfoboxPropertiesRaw", "dbo.WikiInfoboxPropertiesRaw1");

            PivotProperties(connectionString, dbName, "dbo.WikiInfoboxPropertiesRaw1", "dbo.WikiCompanyDataRaw");*/

            //PostProcessWikiCompanyData(connectionString, dbName, "dbo.WikiCompanyDataRaw", "dbo.WikiCompanyData");


            // ----------------------------------------------------------------------------------------
            // Code for loading a SQL table in a C# object
            //var dataset = LoadIntoDataset(connectionString, dbName, "dbo.WikiCompanyDataRaw");

            // ----------------------------------------------------------------------------------------
            // Test complete abstraction by configuring only column transformations (and not the tables)

            //WikiCsvToRaw(connectionString, dbName, csvFilePath, "dbo.WikiInfoboxPropertiesRaw");

            //PivotProperties(connectionString, dbName, "dbo.WikiInfoboxPropertiesRaw1", "dbo.TestWikiCompanyDataRaw");

            Func<string, string> transform = s => string.IsNullOrEmpty(s) ? null : Regex.Replace(s, @"^\{\{profit\}\}", "");
            TransformColumn(connectionString, dbName, "dbo.TestWikiCompanyDataRaw", "revenue", transform);

        }

        private static List<string> GetTableColumns(string connectionString, string db, string table)
        {
            // Read data from SQL server
            var dataTable = new DataTable();
            var conn = new SqlConnection(string.Format("{0};Database={1}", connectionString, db));
            try
            {
                conn.Open();
                var selectQuery = string.Format("SELECT * FROM {0};", table);
                var command = new SqlCommand(selectQuery, conn);
                using (var adapter = new SqlDataAdapter(command))
                {
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw ex;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    conn.Close();
            }
            
            // Put the results in memory (column names, and then data)
            int icolcount = dataTable.Columns.Count;

            var cols = new List<string>();
            for (int i = 0; i < icolcount; i++)
            {
                cols.Add(dataTable.Columns[i].ToString());
            }

            return cols;
        }

        private static Dataset LoadIntoDataset(string connectionString, string dbName, string srcTable)
        {
            // Read data from SQL server
            var dataTable = new DataTable();

            var conn = new SqlConnection(string.Format("{0};Database={1}", connectionString, dbName));
            try
            {
                conn.Open();
                var selectQuery = string.Format("SELECT * FROM {0};", srcTable);
                var command = new SqlCommand(selectQuery, conn);
                using (var adapter = new SqlDataAdapter(command))
                {
                    adapter.Fill(dataTable);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw ex;
            }
            finally
            {
                if (conn.State == ConnectionState.Open)
                    conn.Close();
            }


            // Put the results in memory (column names, and then data)
            int icolcount = dataTable.Columns.Count;

            var cols = new List<string>();
            for (int i = 0; i < icolcount; i++)
            {
                cols.Add(dataTable.Columns[i].ToString());
            }

            var rows = new List<string[]>();
            foreach (DataRow drow in dataTable.Rows)
            {
                var row = new string[icolcount];
                for (int i = 0; i < icolcount; i++)
                {
                    if (!Convert.IsDBNull(drow[i]))
                        row[i] = drow[i].ToString();
                }
                rows.Add(row);
            }

            return new Dataset()
            {
                Columns = cols.ToArray(),
                Data = rows
            };
        }


        private static void EtlTransformation<T, U>(string connectionString, string dbName, string srcTable, string tgtTable,
            DBSource<T> source, RowTransformation<T,U> trans, DBDestination<U> dest)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(connectionString));

            // Create database
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));

            // TODO: move this code which shouldn't be here. 
            // Ideally, tables should configure themselves automatically to avoid this kind of code for schema changes.
            DropTableTask.Drop(tgtTable);
            CreateTableTask.Create(tgtTable, new List<TableColumn>()
            {
                new TableColumn("ID", "int", allowNulls: false, isPrimaryKey:true, isIdentity:true),
                new TableColumn("Name", "nvarchar(max)", allowNulls: true),
                new TableColumn("Revenue", "nvarchar(max)", allowNulls: true),
                new TableColumn("RevenueYear", "nvarchar(max)", allowNulls: true)
            });

            source.LinkTo(trans);
            trans.LinkTo(dest);

            source.Execute();
            dest.Wait();

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in table '{1}'", rowCount, tgtTable);
        }

        private static string TransformColumn(string connectionString, string db, string table, string column, Func<string, string> transform)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(connectionString);

            // Create database (should already exist)
            CreateDatabaseTask.Create(db);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, db)));

            // Create new table, based on the source table schema (auto configuration).
            // TODO: find a better naming logic!
            var newTable = "dbo.newtable";
            // Drop the table just in case. TODO: make sure that table names or unique
            DropTableTask.Drop(newTable);
            var columns = GetTableColumns(connectionString, db, table);
            var tableColumns = columns.Select(col => new TableColumn(col, "nvarchar(max)", allowNulls: true)).ToList();
                        
            CreateTableTask.Create(newTable, tableColumns);


            var source = new DBSource(table);
            Func<string[], string[]> rowTransFunc = arr =>
            {
                arr[185] = transform(arr[185]);
                return arr;
            };
            var trans = new RowTransformation(rowTransFunc);
            var dest = new DBDestination(newTable);

            source.LinkTo(trans);
            trans.LinkTo(dest);

            source.Execute();
            dest.Wait();

            int rowCount = RowCountTask.Count(newTable).Value;
            Console.WriteLine("Inserted {0} rows in table '{1}'", rowCount, newTable);

            return newTable;
        }

        private static void PostProcessWikiCompanyData(string connectionString, string dbName, string srcTable, string tgtTable)
        {
            var source = new DBSource<WikiCompanyData>(string.Format(@"
                select ID, name, revenue, revenue_year
                from {0}", srcTable));
            var regexString = @"\((\d{4})\)";
            var trans = new RowTransformation<WikiCompanyData, WikiCompanyData>(
                myRow => new WikiCompanyData
                {
                    Name = myRow.Name,
                    Revenue = myRow.Revenue == null ? "" : Regex.Replace(myRow.Revenue, regexString, ""),
                    RevenueYear = myRow.Revenue == null ? myRow.RevenueYear : 
                        Regex.IsMatch(myRow.Revenue, regexString) ? Regex.Match(myRow.Revenue, regexString).Groups[1].Value: myRow.RevenueYear
                }); ;
            var dest = new DBDestination<WikiCompanyData>(tgtTable);

            // Execute the data transformation
            EtlTransformation<WikiCompanyData, WikiCompanyData>(connectionString, dbName, srcTable, tgtTable, source, trans, dest);
        }

        private static void WikiCsvToRaw(string connectionString, string dbName, string csvFilePath, string tgtTable)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(connectionString);

            // Create database
            DropDatabaseTask.Drop(dbName);
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));

            DropTableTask.Drop(tgtTable);
            CreateTableTask.Create(tgtTable, new List<TableColumn>()
            {
                new TableColumn("ID", "int", allowNulls: false, isPrimaryKey:true, isIdentity:true),
                new TableColumn("PageTitle", "nvarchar(max)", allowNulls: false),
                new TableColumn("InfoboxId", "nvarchar(max)", allowNulls: true),
                new TableColumn("PropKey", "nvarchar(max)", allowNulls: true),
                new TableColumn("PropValue", "nvarchar(max)", allowNulls: true)
            });

            // Load CSV file into SQL 
            CSVSource source = new CSVSource(csvFilePath);
            source.Configuration.Delimiter = "\t";
            source.SkipRows = 1; // skip header

            var row = new RowTransformation<string[], RawInfoboxProperty>
            (
                // exclude incorrect csv lines to avoid exception below
                input => input.Length != 4 ? null : new RawInfoboxProperty()
                {
                    PageTitle = input[0],
                    InfoboxId = input[1],
                    PropKey = input[2],
                    PropValue = input[3]
                }
            );
            var dest = new DBDestination<RawInfoboxProperty>(tgtTable);

            source.LinkTo(row);
            row.LinkTo(dest);

            source.Execute();
            dest.Wait();

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in {1}", rowCount, tgtTable);
        }

        private static void CleanInfoboxPropertyNames(string connectionString, string dbName, string srcTable, string tgtTable)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(connectionString));

            // Create database
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));

            CreateTableTask.Create(tgtTable, new List<TableColumn>()
            {
                new TableColumn("ID", "int", allowNulls: false, isPrimaryKey:true, isIdentity:true),
                new TableColumn("PageTitle", "nvarchar(max)", allowNulls: false),
                new TableColumn("InfoboxId", "nvarchar(max)", allowNulls: true),
                new TableColumn("PropKey", "nvarchar(max)", allowNulls: true),
                new TableColumn("PropValue", "nvarchar(max)", allowNulls: true)
            });

            var source = new DBSource<RawInfoboxProperty>(string.Format(@"
                select ID, PageTitle, InfoboxId, PropKey, PropValue
                from {0}", srcTable));
            var trans = new RowTransformation<RawInfoboxProperty, RawInfoboxProperty>(
                myRow => new RawInfoboxProperty
                {
                    PageTitle = myRow.PageTitle,
                    InfoboxId = myRow.InfoboxId,
                    PropKey = Regex.Replace(myRow.PropKey, @"[^a-zA-Z]+", "_"),
                    PropValue = myRow.PropValue
                });
            var dest = new DBDestination<RawInfoboxProperty>(tgtTable);

            source.LinkTo(trans);
            trans.LinkTo(dest);

            source.Execute();
            dest.Wait();

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in table '{1}'", rowCount, tgtTable);
        }


        private static void PivotProperties(string connectionString, string dbName, string srcTable, string tgtTable)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(connectionString));

            // Create database
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));

            // Retrieve the list of all the property keys
            var source = new DBSource<PropKeyName>() {
                Sql = string.Format(@"select PropKey from {0} group by PropKey", srcTable)
            };

            var propertyKeys = new List<PropKeyName>();
            var dest = new CustomDestination<PropKeyName>(
                row => {
                    propertyKeys.Add(row);
                }
            );

            source.LinkTo(dest);
            source.Execute();
            dest.Wait();

            // Then create the target table with those keys as columns
            var tableColumns = propertyKeys
                .Where(key => Regex.IsMatch(key.PropKey, @"^[\w_]+$"))
                .Select(key => new TableColumn(key.PropKey, "nvarchar(max)", allowNulls: true))
                .ToList();
            tableColumns.Insert(0, new TableColumn("PageTitle", "nvarchar(max)", allowNulls: true));
            //tableColumns.Insert(0, new TableColumn("ID", "int", allowNulls: false, isPrimaryKey: true, isIdentity: true));

            // Copy the table
            DropTableTask.Drop(tgtTable);
            CreateTableTask.Create(tgtTable, tableColumns);

            var sqlPivotQuery = string.Format(@"DECLARE @colsPivot NVARCHAR(max),
	@query NVARCHAR(max);

select @colsPivot = STUFF((SELECT  ',' 
                      + quotename(PropKey)
					from {0}
					where PropKey is not NULL
					group by PropKey
            FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)') 
        ,1,1,'')

set @query = 
'INSERT INTO {1} (PageTitle, ' + @colsPivot + ')
select PageTitle, ' + @colsPivot + '
from (
	select PropKey, PropValue, InfoboxId, PageTitle
	from {0}
) as Props
PIVOT (MIN(PropValue)
	FOR PropKey IN (' + @colsPivot + '))
as PVT'

exec(@query)", srcTable, tgtTable);
            SqlTask.ExecuteNonQuery("Pivot properties", sqlPivotQuery);

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in table '{1}'", rowCount, tgtTable);
        }

        private static void DeleteEmptyProperties(string connectionString, string dbName, string srcTable, string tgtTable)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(connectionString));

            // Create database
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));

            // Copy the table
            DropTableTask.Drop(tgtTable);
            CreateTableTask.Create(tgtTable, new List<TableColumn>()
            {
                new TableColumn("ID", "int", allowNulls: false, isPrimaryKey:true, isIdentity:true),
                new TableColumn("PageTitle", "nvarchar(max)", allowNulls: false),
                new TableColumn("InfoboxId", "nvarchar(max)", allowNulls: true),
                new TableColumn("PropKey", "nvarchar(max)", allowNulls: true),
                new TableColumn("PropValue", "nvarchar(max)", allowNulls: true)
            });

            var source = new DBSource<RawInfoboxProperty>(string.Format(@"
                select ID, PageTitle, InfoboxId, PropKey, PropValue 
                from {0}", srcTable));
            var trans = new RowTransformation<RawInfoboxProperty, RawInfoboxProperty>(
                myRow => myRow);
            var dest = new DBDestination<RawInfoboxProperty>(tgtTable);

            source.LinkTo(trans);
            trans.LinkTo(dest);

            source.Execute();
            dest.Wait();


            SqlTask.ExecuteNonQuery("DROP empty properties", string.Format("DELETE FROM {0} WHERE PropValue = ''", tgtTable));

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in table '{1}'", rowCount, tgtTable);
        }

        // Forbes data ETL ------------------------------------------------------------------------------------------------------

        public static void ForbesDataEtl()
        {
            var builder = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json");
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();
            var connectionString = config["connectionString"];

            var timestamp = DateTime.Now.ToString("yyyyMMdd");
            var dbName = string.Format("tucdb_{0}", timestamp);

            var csvFilePath = "C:/Users/Alex/Documents/Github/WikimediaParser/CompanyScrapper/Results/forbes-top-2000-companies-2018.csv";

            ForbesCsvToRaw(connectionString, dbName, csvFilePath, "dbo.Forbes2018Raw");
            Console.WriteLine("=======================");
            ForbesRawToCompanyDb(connectionString, dbName, "dbo.Forbes2018Raw", "dbo.Forbes2018");
        }

        private static void ForbesCsvToRaw(string connectionString, string dbName, string csvFilePath, string tgtTable)
        {            
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(connectionString);

            // Create database
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));
            CreateTableTask.Create(tgtTable, new List<TableColumn>()
            {
                new TableColumn("ID", "int", allowNulls: false, isPrimaryKey:true, isIdentity:true),
                new TableColumn("Name", "nvarchar(max)", allowNulls: false),
                new TableColumn("Rank", "int", allowNulls: true),
                new TableColumn("Position", "int", allowNulls: true),
                new TableColumn("Uri", "nvarchar(max)", allowNulls: true),
                new TableColumn("ImageUri", "nvarchar(max)", allowNulls: true),
                new TableColumn("Industry", "nvarchar(50)", allowNulls: true),
                new TableColumn("Country", "nvarchar(50)", allowNulls: true),
                new TableColumn("Revenue", "decimal", allowNulls: true),
                new TableColumn("MarketValue", "decimal", allowNulls: true),
                new TableColumn("Headquarters", "nvarchar(max)", allowNulls: true),
                new TableColumn("Ceo", "nvarchar(50)", allowNulls: true),
                new TableColumn("Profits", "decimal", allowNulls: true),
                new TableColumn("Assets", "decimal", allowNulls: true),
                new TableColumn("State", "nvarchar(50)", allowNulls: true),
                new TableColumn("SquareImage", "nvarchar(max)", allowNulls: true),
                new TableColumn("Thumbnail", "nvarchar(max)", allowNulls: true)
            });

            // Load CSV file into SQL 
            CSVSource source = new CSVSource(csvFilePath);
            source.Configuration.Delimiter = ";";

            RowTransformation<string[], ForbesCompanyData> row = new RowTransformation<string[], ForbesCompanyData>
            (
                input => new ForbesCompanyData(){
                    Position = int.Parse(input[0]),
                    Rank = int.Parse(input[1]),
                    Name = input[2],
                    Uri = input[3],
                    ImageUri = input[4],
                    Industry = input[5],
                    Country = input[6],
                    Revenue = decimal.Parse(input[7]),
                    MarketValue = decimal.Parse(input[8]),
                    Headquarters = input[9],
                    Ceo = input[10],
                    Profits = decimal.Parse(input[11]),
                    Assets = decimal.Parse(input[12]),
                    State = input[13],
                    SquareImage = input[14],
                    Thumbnail = input[15]
                }
            );
            DBDestination<ForbesCompanyData> dest = new DBDestination<ForbesCompanyData>(tgtTable);

            source.LinkTo(row);
            row.LinkTo(dest);

            source.Execute();
            dest.Wait();

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in {1}", rowCount, tgtTable);
        }

        private static void ForbesRawToCompanyDb(string connectionString, string dbName, string srcTable, string tgtTable)
        {
            // Create control flow
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(connectionString));

            // Create database
            CreateDatabaseTask.Create(dbName);

            // Create table for Forbes 2018 company data
            ControlFlow.CurrentDbConnection = new SqlConnectionManager(new ConnectionString(string.Format("{0};Initial Catalog={1}", connectionString, dbName)));

            CreateTableTask.Create(tgtTable, new List<TableColumn>()
            {
                new TableColumn("ID", "int", allowNulls: false, isPrimaryKey:true, isIdentity:true),
                new TableColumn("Name", "nvarchar(max)", allowNulls: false),
                new TableColumn("Industry", "nvarchar(50)", allowNulls: true),
                new TableColumn("Country", "nvarchar(50)", allowNulls: true),
                new TableColumn("Revenue_2018_mUSD", "decimal", allowNulls: true),
                new TableColumn("MarketValue_2018_mUSD", "decimal", allowNulls: true),
                new TableColumn("Headquarters", "nvarchar(max)", allowNulls: true),
                new TableColumn("Ceo_2018", "nvarchar(50)", allowNulls: true),
                new TableColumn("Profits_2018_mUSD", "decimal", allowNulls: true),
                new TableColumn("Assets_2018_mUSD", "decimal", allowNulls: true)
            });
            
            var source = new DBSource<ForbesCompanyData>(string.Format(@"
                select ID, Name, Rank, Position, Uri, ImageUri, Industry, Country, Revenue, MarketValue, Headquarters, Ceo, Profits, Assets, State, SquareImage, Thumbnail 
                from {0}", srcTable));
            var trans = new RowTransformation<ForbesCompanyData, CompanyData>(
                myRow => new CompanyData {
                     Name = myRow.Name,
                     Assets_2018_mUSD = myRow.Assets,
                     Profits_2018_mUSD = myRow.Profits,
                     Revenue_2018_mUSD = myRow.Revenue,
                     Industry = myRow.Industry,
                     Country = myRow.Country,
                     MarketValue_2018_mUSD = myRow.MarketValue,
                     Ceo_2018 = myRow.Ceo,
                     Headquarters = myRow.Headquarters != myRow.Country ? string.Format("{0}, {1}", myRow.Headquarters, myRow.Country): myRow.Country
                });
            var dest = new DBDestination<CompanyData>(tgtTable);

            source.LinkTo(trans);
            trans.LinkTo(dest);

            source.Execute();
            dest.Wait();

            int rowCount = RowCountTask.Count(tgtTable).Value;
            Console.WriteLine("Inserted {0} rows in table '{1}'", rowCount, tgtTable);
        }

    }
}
