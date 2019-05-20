﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeslaLogger
{
    class DBHelper
    {
        public static CurrentJSON currentJSON = new CurrentJSON();

        public static bool current_is_preconditioning = false;

        public static string DBConnectionstring
        {
            get
            {
                if (String.IsNullOrEmpty(ApplicationSettings.Default.DBConnectionstring))
                    return "Server=localhost;Database=teslalogger;Uid=root;Password=teslalogger;";

                return ApplicationSettings.Default.DBConnectionstring;
            }
        }

        public static void CloseState(int maxPosid)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("update state set EndDate = @enddate, EndPos = @EndPos where EndDate is null", con);
                cmd.Parameters.AddWithValue("@enddate", DateTime.Now);
                cmd.Parameters.AddWithValue("@EndPos", maxPosid);
                cmd.ExecuteNonQuery();
            }

            currentJSON.CreateCurrentJSON();
        }

        public static void StartState(string state)
        {
            if (state != null)
            {
                if (state == "online")
                {
                    currentJSON.current_online = true;
                    currentJSON.current_sleeping = false;
                }
                else if (state == "asleep")
                {
                    currentJSON.current_online = false;
                    currentJSON.current_sleeping = true;
                }
            }

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();

                MySqlCommand cmd1 = new MySqlCommand("select state from state where EndDate is null", con);
                MySqlDataReader dr = cmd1.ExecuteReader();
                if (dr.Read())
                {
                    if (dr[0].ToString() == state)
                        return;
                }
                dr.Close();

                int MaxPosid = GetMaxPosid();
                CloseState(MaxPosid);

                Tools.Log("state: " + state);

                MySqlCommand cmd = new MySqlCommand("insert state (StartDate, state, StartPos) values (@StartDate, @state, @StartPos)", con);
                cmd.Parameters.AddWithValue("@StartDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@state", state);
                cmd.Parameters.AddWithValue("@StartPos", MaxPosid);
                cmd.ExecuteNonQuery();
            }
        }

        public static void CloseChargingState()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("update chargingstate set EndDate = @EndDate, EndChargingID = @EndChargingID where EndDate is null", con);
                cmd.Parameters.AddWithValue("@EndDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@EndChargingID", GetMaxChargeid());
                cmd.ExecuteNonQuery();
            }

            currentJSON.current_charging = false;
            currentJSON.current_charger_power = 0;
            currentJSON.current_charger_voltage = 0;
            currentJSON.current_charger_phases = 0;
            currentJSON.current_charger_actual_current = 0;
        }

        internal static void GetEconomy_Wh_km(WebHelper wh)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand(@"SELECT  count(*) as anz, round(charging_End.charge_energy_added / (charging_End.ideal_battery_range_km - charging.ideal_battery_range_km), 3) AS economy_Wh_km
                        FROM charging inner JOIN chargingstate ON charging.id = chargingstate.StartChargingID 
                        LEFT OUTER JOIN charging AS charging_End ON chargingstate.EndChargingID = charging_End.id
                        where TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 100 
                        and chargingstate.EndChargingID - chargingstate.StartChargingID > 4 
                        and charging_End.battery_level <= 90
                        group by economy_Wh_km
                        order by anz desc
                        limit 1 ", con);
                    MySqlDataReader dr = cmd.ExecuteReader();

                    if (dr.Read())
                    {
                        long anz = (long)dr["anz"];
                        double wh_km = (double)dr["economy_Wh_km"];

                        Tools.Log($"Economy from DB: {wh_km} Wh/km - count: {anz}");

                        wh.carSettings.DB_Wh_TR = wh_km.ToString();
                        wh.carSettings.DB_Wh_TR_count = anz.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log(ex.ToString());
            }
        }

        internal static void GetLastTrip()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("SELECT * FROM trip order by StartDate desc limit 1", con);
                    MySqlDataReader dr = cmd.ExecuteReader();

                    if (dr.Read())
                    {
                        currentJSON.current_trip_start = (DateTime)dr["StartDate"];
                        currentJSON.current_trip_end = (DateTime)dr["EndDate"];
                        currentJSON.current_trip_km_start = Convert.ToDouble(dr["StartKm"]);
                        currentJSON.current_trip_km_end = Convert.ToDouble(dr["EndKm"]);
                        currentJSON.current_trip_max_speed = Convert.ToDouble(dr["speed_max"]);
                        currentJSON.current_trip_max_power = Convert.ToDouble(dr["power_max"]);
                        currentJSON.current_trip_start_range = Convert.ToDouble(dr["StartRange"]);
                        currentJSON.current_trip_end_range = Convert.ToDouble(dr["EndRange"]);
                        currentJSON.CreateCurrentJSON();
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log(ex.ToString());
            }
        }

        public static void StartChargingState()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("insert chargingstate (StartDate, Pos, StartChargingID) values (@StartDate, @Pos, @StartChargingID)", con);
                cmd.Parameters.AddWithValue("@StartDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@Pos", GetMaxPosid());
                cmd.Parameters.AddWithValue("@StartChargingID", GetMaxChargeid() + 1);
                cmd.ExecuteNonQuery();
            }

            currentJSON.current_charging = true;
            currentJSON.CreateCurrentJSON();
        }

        public static void CloseDriveState(DateTime EndDate)
        {
            int StartPos = 0;
            int MaxPosId = GetMaxPosid();

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("select StartPos from drivestate where EndDate is null", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                if (dr.Read())
                {
                    StartPos = Convert.ToInt32(dr[0]);
                }
                dr.Close();
            }

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("update drivestate set EndDate = @EndDate, EndPos = @Pos where EndDate is null", con);
                cmd.Parameters.AddWithValue("@EndDate", EndDate);
                cmd.Parameters.AddWithValue("@Pos", MaxPosId);
                cmd.ExecuteNonQuery();
            }

            if (StartPos != 0)
                UpdateDriveStatistics(StartPos, MaxPosId);

            currentJSON.current_driving = false;
            currentJSON.current_speed = 0;
            currentJSON.current_power = 0;
        }

        public static void UpdateAddress(int posid)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("select lat, lng from pos where id = @id", con);
                    cmd.Parameters.AddWithValue("@id", posid);
                    MySqlDataReader dr = cmd.ExecuteReader();
                    if (dr.Read())
                    {
                        double lat = Convert.ToDouble(dr[0]);
                        double lng = Convert.ToDouble(dr[1]);
                        dr.Close();

                        WebHelper.ReverseGecocodingAsync(lat, lng).ContinueWith(task =>
                        {
                            try
                            {
                                using (MySqlConnection con2 = new MySqlConnection(DBConnectionstring))
                                {
                                    con2.Open();
                                    MySqlCommand cmd2 = new MySqlCommand("update pos set address = @adress where id = @id", con2);
                                    cmd2.Parameters.AddWithValue("@id", posid);
                                    cmd2.Parameters.AddWithValue("@adress", task.Result);
                                    cmd2.ExecuteNonQuery();
                                }
                            }
                            catch (Exception ex)
                            {
                                Tools.Log(ex.ToString());
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log(ex.ToString());
            }
        }

        private static void UpdateDriveStatistics(int startPos, int endPos, bool logging = false)
        {
            try
            {
                if (logging)
                    Tools.Log("UpdateDriveStatistics");

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("SELECT avg(outside_temp) as outside_temp_avg, max(speed) as speed_max, max(power) as power_max, min(power) as power_min, avg(power) as power_avg FROM pos where id between @startpos and @endpos", con);
                    cmd.Parameters.AddWithValue("@startpos", startPos);
                    cmd.Parameters.AddWithValue("@endpos", endPos);

                    MySqlDataReader dr = cmd.ExecuteReader();
                    if (dr.Read())
                    {
                        using (MySqlConnection con2 = new MySqlConnection(DBConnectionstring))
                        {
                            con2.Open();
                            MySqlCommand cmd2 = new MySqlCommand("update drivestate set outside_temp_avg=@outside_temp_avg, speed_max=@speed_max, power_max=@power_max, power_min=@power_min, power_avg=@power_avg where StartPos=@StartPos and EndPos=@EndPos  ", con2);
                            cmd2.Parameters.AddWithValue("@StartPos", startPos);
                            cmd2.Parameters.AddWithValue("@EndPos", endPos);

                            cmd2.Parameters.AddWithValue("@outside_temp_avg", dr["outside_temp_avg"]);
                            cmd2.Parameters.AddWithValue("@speed_max", dr["speed_max"]);
                            cmd2.Parameters.AddWithValue("@power_max", dr["power_max"]);
                            cmd2.Parameters.AddWithValue("@power_min", dr["power_min"]);
                            cmd2.Parameters.AddWithValue("@power_avg", dr["power_avg"]);

                            cmd2.ExecuteNonQuery();
                        }
                    }
                }

                // If Startpos doesn't have an "ideal_battery_rage_km", it will be updated from the first valid dataset
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("SELECT * FROM pos where id = @startpos", con);
                    cmd.Parameters.AddWithValue("@startpos", startPos);

                    MySqlDataReader dr = cmd.ExecuteReader();
                    if (dr.Read())
                    {
                        if (dr["ideal_battery_range_km"] == DBNull.Value)
                        {
                            DateTime dt1 = (DateTime)dr["Datum"];
                            dr.Close();

                            cmd = new MySqlCommand("SELECT * FROM pos where id > @startPos and ideal_battery_range_km is not null and battery_level is not null order by id asc limit 1", con);
                            cmd.Parameters.AddWithValue("@startPos", startPos);
                            dr = cmd.ExecuteReader();

                            if (dr.Read())
                            {
                                DateTime dt2 = (DateTime)dr["Datum"];
                                TimeSpan ts = dt2 - dt1;

                                object ideal_battery_range_km = dr["ideal_battery_range_km"];
                                object battery_level = dr["battery_level"];

                                if (ts.TotalSeconds < 120)
                                {
                                    dr.Close();

                                    cmd = new MySqlCommand("update pos set ideal_battery_range_km = @ideal_battery_range_km, battery_level = @battery_level where id = @startPos", con);
                                    cmd.Parameters.AddWithValue("@startPos", startPos);
                                    cmd.Parameters.AddWithValue("@ideal_battery_range_km", ideal_battery_range_km.ToString());
                                    cmd.Parameters.AddWithValue("@battery_level", battery_level.ToString());
                                    cmd.ExecuteNonQuery();

                                    Tools.Log($"Trip from {dt1} ideal_battery_range_km updated!");
                                }
                                else
                                {
                                    Tools.Log($"Trip from {dt1} ideal_battery_range_km is NULL, but last valid data is too old: {dt2}!");
                                }
                            }
                        }
                    }
                }


                // If Endpos doesn't have an "ideal_battery_rage_km", it will be updated from the last valid dataset
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("SELECT * FROM pos where id = @endpos", con);
                    cmd.Parameters.AddWithValue("@endpos", endPos);

                    MySqlDataReader dr = cmd.ExecuteReader();
                    if (dr.Read())
                    {
                        if (dr["ideal_battery_range_km"] == DBNull.Value)
                        {
                            DateTime dt1 = (DateTime)dr["Datum"];
                            dr.Close();

                            cmd = new MySqlCommand("SELECT * FROM pos where id < @endpos and ideal_battery_range_km is not null and battery_level is not null order by id desc limit 1", con);
                            cmd.Parameters.AddWithValue("@endpos", endPos);
                            dr = cmd.ExecuteReader();

                            if (dr.Read())
                            {
                                DateTime dt2 = (DateTime)dr["Datum"];
                                TimeSpan ts = dt1 - dt2;

                                object ideal_battery_range_km = dr["ideal_battery_range_km"];
                                object battery_level = dr["battery_level"];

                                if (ts.TotalSeconds < 120)
                                {
                                    dr.Close();

                                    cmd = new MySqlCommand("update pos set ideal_battery_range_km = @ideal_battery_range_km, battery_level = @battery_level where id = @endpos", con);
                                    cmd.Parameters.AddWithValue("@endpos", endPos);
                                    cmd.Parameters.AddWithValue("@ideal_battery_range_km", ideal_battery_range_km.ToString());
                                    cmd.Parameters.AddWithValue("@battery_level", battery_level.ToString());
                                    cmd.ExecuteNonQuery();

                                    Tools.Log($"Trip from {dt1} ideal_battery_range_km updated!");
                                }
                                else
                                {
                                    Tools.Log($"Trip from {dt1} ideal_battery_range_km is NULL, but last valid data is too old: {dt2}!");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log(ex.ToString());
            }
        }

        public static bool UpdateIncompleteTrips()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand(@"SELECT pos_start.id as StartPos, pos_end.id as EndPos
                     FROM drivestate 
                     JOIN pos pos_start ON drivestate . StartPos = pos_start. id
                     JOIN pos pos_end ON  drivestate . EndPos = pos_end. id 
                     WHERE
                     (pos_end. odometer - pos_start. odometer ) > 0.1 and 
                     (( pos_start. ideal_battery_range_km is null) or ( pos_end. ideal_battery_range_km is null))", con);
                    MySqlDataReader dr = cmd.ExecuteReader();

                    while (dr.Read())
                    {
                        try
                        {
                            int StartPos = Convert.ToInt32(dr[0]);
                            int EndPos = Convert.ToInt32(dr[1]);

                            DBHelper.UpdateDriveStatistics(StartPos, EndPos, false);
                        }
                        catch (Exception ex)
                        {
                            Tools.Log(ex.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.ExceptionWriter(ex, "UpdateIncompleteTrips");
            }

            return false;
        }

        public static void UpdateAllDrivestateData()
        {
            Tools.Log("UpdateAllDrivestateData start");

            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("select StartPos,EndPos from drivestate", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    try
                    {
                        int StartPos = Convert.ToInt32(dr[0]);
                        int EndPos = Convert.ToInt32(dr[1]);

                        DBHelper.UpdateDriveStatistics(StartPos, EndPos, false);
                    }
                    catch (Exception ex)
                    {
                        Tools.Log(ex.ToString());
                    }
                }
            }

            Tools.Log("UpdateAllDrivestateData end");
        }

        public static void StartDriveState()
        {
            Tools.Log("StartDriveState");

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("insert drivestate (StartDate, StartPos) values (@StartDate, @Pos)", con);
                cmd.Parameters.AddWithValue("@StartDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@Pos", GetMaxPosid());
                cmd.ExecuteNonQuery();
            }

            currentJSON.current_driving = true;
            currentJSON.current_charge_energy_added = 0;
            currentJSON.current_trip_start = DateTime.Now;
            currentJSON.current_trip_end = DateTime.MinValue;
            currentJSON.current_trip_km_start = 0;
            currentJSON.current_trip_km_end = 0;
            currentJSON.current_trip_max_speed = 0;
            currentJSON.current_trip_max_power = 0;
            currentJSON.current_trip_start_range = 0;
            currentJSON.current_trip_end_range = 0;

            currentJSON.CreateCurrentJSON();
        }


        internal static void InsertPos(string timestamp, double latitude, double longitude, int speed, decimal power, double odometer, double ideal_battery_range_km, int battery_level, double? outside_temp, string altitude)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                
                MySqlCommand cmd = new MySqlCommand("insert pos (Datum, lat, lng, speed, power, odometer, ideal_battery_range_km, outside_temp, altitude, battery_level) values (@Datum, @lat, @lng, @speed, @power, @odometer, @ideal_battery_range_km, @outside_temp, @altitude, @battery_level)", con);
                cmd.Parameters.AddWithValue("@Datum", UnixToDateTime(long.Parse(timestamp)).ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@lat", latitude.ToString());
                cmd.Parameters.AddWithValue("@lng", longitude.ToString());
                cmd.Parameters.AddWithValue("@speed", (int)((decimal)speed * 1.60934M));
                cmd.Parameters.AddWithValue("@power", (int)(power * 1.35962M));
                cmd.Parameters.AddWithValue("@odometer", odometer.ToString());

                if (ideal_battery_range_km == -1)
                    cmd.Parameters.AddWithValue("@ideal_battery_range_km", DBNull.Value);
                else
                    cmd.Parameters.AddWithValue("@ideal_battery_range_km", ideal_battery_range_km.ToString());

                if (outside_temp == null)
                    cmd.Parameters.AddWithValue("@outside_temp", DBNull.Value);
                else
                    cmd.Parameters.AddWithValue("@outside_temp", ((double)outside_temp).ToString());

                if (altitude.Length == 0)
                    cmd.Parameters.AddWithValue("@altitude", DBNull.Value);
                else
                    cmd.Parameters.AddWithValue("@altitude", altitude);

                if (battery_level == -1)
                    cmd.Parameters.AddWithValue("@battery_level", DBNull.Value);
                else
                    cmd.Parameters.AddWithValue("@battery_level", battery_level.ToString());

                cmd.ExecuteNonQuery();

                try
                {
                    currentJSON.current_speed = (int)((decimal)speed * 1.60934M);
                    currentJSON.current_power = (int)(power * 1.35962M);
                    currentJSON.current_odometer = odometer;
                    currentJSON.current_ideal_battery_range_km = ideal_battery_range_km;

                    if (currentJSON.current_trip_km_start == 0)
                    {
                        currentJSON.current_trip_km_start = odometer;
                        currentJSON.current_trip_start_range = currentJSON.current_ideal_battery_range_km;
                    }

                    currentJSON.current_trip_max_speed = Math.Max(currentJSON.current_trip_max_speed, currentJSON.current_speed);
                    currentJSON.current_trip_max_power = Math.Max(currentJSON.current_trip_max_power, currentJSON.current_power);

                }
                catch (Exception ex)
                {
                    Tools.Log(ex.ToString());
                }
            }

            currentJSON.CreateCurrentJSON();
        }

        static DateTime lastChargingInsert = DateTime.Today;


        internal static void InsertCharging(string timestamp, string battery_level, string charge_energy_added, string charger_power, double ideal_battery_range, string charger_voltage, string charger_phases, string charger_actual_current, double? outside_temp, bool forceinsert, string charger_pilot_current, string charge_current_request)
        {
            Tools.SetThread_enUS();

            if (charger_phases == "")
                charger_phases = "1";

            double kmRange = ideal_battery_range / (double)0.62137;

            double powerkW = Convert.ToDouble(charger_power);
            double waitbetween2pointsdb = 1000.0 / powerkW;

            double deltaSeconds = (DateTime.Now - lastChargingInsert).TotalSeconds;

            if (forceinsert || deltaSeconds > waitbetween2pointsdb)
            {
                lastChargingInsert = DateTime.Now;

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand("insert charging (Datum, battery_level, charge_energy_added, charger_power, ideal_battery_range_km, charger_voltage, charger_phases, charger_actual_current, outside_temp, charger_pilot_current, charge_current_request) values (@Datum, @battery_level, @charge_energy_added, @charger_power, @ideal_battery_range_km, @charger_voltage, @charger_phases, @charger_actual_current, @outside_temp, @charger_pilot_current, @charge_current_request)", con);
                    cmd.Parameters.AddWithValue("@Datum", UnixToDateTime(long.Parse(timestamp)).ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@battery_level", battery_level);
                    cmd.Parameters.AddWithValue("@charge_energy_added", charge_energy_added);
                    cmd.Parameters.AddWithValue("@charger_power", charger_power);
                    cmd.Parameters.AddWithValue("@ideal_battery_range_km", kmRange.ToString());
                    cmd.Parameters.AddWithValue("@charger_voltage", int.Parse(charger_voltage));
                    cmd.Parameters.AddWithValue("@charger_phases", charger_phases);
                    cmd.Parameters.AddWithValue("@charger_actual_current", charger_actual_current);

                    int i = 0;

                    if (charger_pilot_current != null && int.TryParse(charger_pilot_current, out i))
                        cmd.Parameters.AddWithValue("@charger_pilot_current", i);
                    else
                        cmd.Parameters.AddWithValue("@charger_pilot_current", DBNull.Value);

                    if (charge_current_request != null && int.TryParse(charge_current_request, out i))
                        cmd.Parameters.AddWithValue("@charge_current_request", i);
                    else
                        cmd.Parameters.AddWithValue("@charge_current_request", DBNull.Value);

                    if (outside_temp == null)
                        cmd.Parameters.AddWithValue("@outside_temp", DBNull.Value);
                    else
                        cmd.Parameters.AddWithValue("@outside_temp", ((double)outside_temp).ToString());

                    cmd.ExecuteNonQuery();
                }
            }

            try
            {
                currentJSON.current_battery_level = Convert.ToInt32(battery_level);
                currentJSON.current_charge_energy_added = Convert.ToDouble(charge_energy_added);
                currentJSON.current_charger_power = Convert.ToInt32(charger_power);
                currentJSON.current_ideal_battery_range_km = kmRange;
                currentJSON.current_charger_voltage = int.Parse(charger_voltage);
                currentJSON.current_charger_phases = Convert.ToInt32(charger_phases);
                currentJSON.current_charger_actual_current = Convert.ToInt32(charger_actual_current);
                currentJSON.CreateCurrentJSON();
            }
            catch (Exception ex)
            {
                Tools.Log(ex.ToString());
            }
        }

        public static DateTime UnixToDateTime(long t)
        {
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dt = dt.AddMilliseconds(t);
            dt = dt.ToLocalTime();
            return dt;

        }

        public static int CountPos()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("Select count(*) from pos", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                if (dr.Read())
                    return Convert.ToInt32(dr[0]);
            }

            return 0;
        }

        public static int GetMaxPosid(bool withReverseGeocoding = true)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("Select max(id) from pos", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                if (dr.Read() && dr[0] != DBNull.Value)
                { 
                    int pos = Convert.ToInt32(dr[0]);
                    if (withReverseGeocoding)
                        UpdateAddress(pos);

                    return pos;
                }
            }

            return 0;
        }

        static int GetMaxChargeid()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("Select max(id) from charging", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                if (dr.Read() && dr[0] != DBNull.Value)
                    return Convert.ToInt32(dr[0]);
            }

            return 0;
        }

        public static string GetVersion()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("SELECT @@version", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                if (dr.Read())
                    return dr[0].ToString();
            }

            return "NULL";
        }

        

        public static bool ColumnExists(string table, string column)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                MySqlCommand cmd = new MySqlCommand("SHOW COLUMNS FROM `"+ table +"` LIKE '"+ column +"';", con);
                MySqlDataReader dr = cmd.ExecuteReader();
                if (dr.Read())
                    return true;
            }

            return false;
        }

        public static int ExecuteSQLQuery(string sql)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    MySqlCommand cmd = new MySqlCommand(sql, con);
                    return cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Tools.ExceptionWriter(ex, sql);
                throw;
            }
        }
    }
}
