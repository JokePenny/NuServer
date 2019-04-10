using System;
using System.Data.SqlClient;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Server.src
{
    class ServerObject
    {
        static string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        const int port = 21;
        TcpListener listener;
        List<ClientObject> clients = new List<ClientObject>(); // все подключения

        protected internal void AddConnection(ClientObject clientObject)
        {
            clients.Add(clientObject);
        }

        protected internal void RemoveConnection(string id)
        {
            // получаем по id закрытое подключение
            ClientObject client = clients.FirstOrDefault(c => c.Id == id);
            // и удаляем его из списка подключений
            if (client != null)
                clients.Remove(client);
        }

        // прослушивание входящих подключений
        protected internal void Listen()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Console.WriteLine("Сервер запущен. Ожидание подключений...\r\n");

                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();

                    ClientObject clientObject = new ClientObject(client, this);
                    // создаем новый поток для обслуживания нового клиента
                    Thread clientThread = new Thread(new ThreadStart(clientObject.Process));
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Disconnect();
            }
        }

        // трансляция сообщения подключенным клиентам
        protected internal void BroadcastMessage(string message, string id, string room, string name)
        {
            byte[] data = Encoding.Unicode.GetBytes(message);
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i].Id != id && clients[i].located == room && clients[i].userName == name) // если id клиента не равно id отправляющего
                {
                    clients[i].Stream.Write(data, 0, data.Length); //передача данных
                }
            }
        }

        protected internal void BroadcastMessage(string message, string id, string room)
        {
            byte[] data = Encoding.Unicode.GetBytes(message);
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i].Id != id && clients[i].located == room) // если id клиента не равно id отправляющего
                {
                    clients[i].Stream.Write(data, 0, data.Length); //передача данных
                }
            }
        }
        // отключение всех клиентов
        protected internal void Disconnect()
        {
            listener.Stop(); //остановка сервера

            for (int i = 0; i < clients.Count; i++)
            {
                clients[i].Close(); //отключение клиента
            }
            Environment.Exit(0); //завершение процесса
        }

        protected internal string CommandDecryption(string[] command)
        {
            SqlConnection connectionUser = null;
            SqlCommand cmd = null;
            SqlDataReader row = null;
            string message = "";
            switch (command[0])
            {
                case "0": // регистрация
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [Users] WHERE Login = '" + command[1] + "'", connectionUser);
                    row = cmd.ExecuteReader();

                    if (row.HasRows) // если есть данные
                    {
                        connectionUser.Close();
                        row.Close();
                        return "Данный логин уже зарегистрирован";
                    }
                    row.Close();

                    cmd = new SqlCommand("INSERT INTO [Users] ([Login], [Password], [Level_user], [Status]) VALUES (@login, @password, @level_user, @status)", connectionUser);
                    cmd.Parameters.Add("@login", command[1]);
                    cmd.Parameters.Add("@password", command[2]);
                    cmd.Parameters.Add("@level_user", "1");
                    cmd.Parameters.Add("@status", "0");
                    cmd.ExecuteNonQuery();
                    connectionUser.Close();
                    return "Регистрация прошла успешно";
                case "1": // проверка на подлиность данных (пароль логин) вход в игру
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [Users] WHERE [Login] = '" + command[1] + "'", connectionUser);
                    row = cmd.ExecuteReader();
                    if (row.HasRows) // если есть данные
                    {
                        while (row.Read()) // построчно считываем данные
                        {
                            if (row["login"].ToString() == command[1])
                            {
                                object password = row["password"];
                                object level = row["level_user"];
                                object status = row["status"];
                                row.Close();
                                if ("1" == status.ToString() && command[1] != "Admin")
                                {
                                    connectionUser.Close();
                                    return "Данный лог уже активен";
                                }
                                if (command[2] == password.ToString()) // ставим статус юзера на 1 - он онлайн
                                {
                                    cmd = new SqlCommand("UPDATE [Users] SET status = '1' WHERE Login = '" + command[1] + "'", connectionUser);
                                    cmd.ExecuteNonQuery();
                                    connectionUser.Close();
                                    return "Вход выполнен;" + level.ToString();
                                }
                                connectionUser.Close();
                                return "Пароль неверный";
                            }
                        }
                        return "Данных о пользователе нет";
                    }
                    row.Close();
                    connectionUser.Close();
                    return "Данный логин незарегистрирован";
                case "2": //создание комнаты и ее регистрация в списке комнат
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [Rooms] WHERE Name = '" + command[1] + "'", connectionUser);
                    row = cmd.ExecuteReader();

                    if (row.HasRows) // если есть данные
                    {
                        connectionUser.Close();
                        row.Close();
                        return "Комната с таким именем уже существует";
                    }
                    row.Close();

                    if (command[1] != "Users" && command[1] != "Rooms" && command[1] != "NuclearDb")
                    {
                        cmd = new SqlCommand("INSERT INTO [Rooms] ([Name], [Map], [Count_capacity], [Count_connect], [Range_up], [Range_down], [Status]) VALUES (@name, @map, @count_capacity, @count_connect, @range_up, @range_down, @status)", connectionUser);
                        cmd.Parameters.Add("@name", command[1]);
                        cmd.Parameters.Add("@map", command[5]);
                        cmd.Parameters.Add("@count_capacity", command[2]);
                        cmd.Parameters.Add("@count_connect", "1");
                        cmd.Parameters.Add("@range_up", command[3]);
                        cmd.Parameters.Add("@range_down", command[4]);
                        cmd.Parameters.Add("@status", "0");
                        cmd.ExecuteNonQuery();

                        cmd = new SqlCommand("CREATE TABLE " + command[1] + " (UserID int not null identity(1,1) primary key, login varchar(20) not null, level_user int not null, damage int not null, coordx int not null, coordy int null, run int not null, status int not null, readiness int not null)", connectionUser);
                        cmd.ExecuteNonQuery();
                        cmd = new SqlCommand("INSERT INTO [" + command[1] + "] ([Login], [Level_user], [Damage], [Coordx], [Coordy], [Run], [Status], [Readiness]) VALUES (@login, @level_user, @damage, @coordx, @coordy, @run, @status, @readiness)", connectionUser);
                        cmd.Parameters.Add("@login", command[6]);
                        cmd.Parameters.Add("@level_user", command[7]);
                        cmd.Parameters.Add("@damage", "0");
                        cmd.Parameters.Add("@coordx", "0");
                        cmd.Parameters.Add("@coordy", "0");
                        cmd.Parameters.Add("@run", "0");
                        cmd.Parameters.Add("@status", "1");
                        cmd.Parameters.Add("@readiness", "1");
                        cmd.ExecuteNonQuery();
                        connectionUser.Close();
                        return "Комната успешно создана";
                    }
                    cmd.ExecuteNonQuery();
                    connectionUser.Close();
                    return "Данное название недоступно";
                case "3": // вход в комнату (проверка на заполненость и соответствие ранжировнаию)
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [Rooms] WHERE Name = '" + command[1] + "'", connectionUser);
                    row = cmd.ExecuteReader();
                    if (row.HasRows) // если есть данные
                    {
                        while (row.Read()) // построчно считываем данные
                        {
                            if (row["name"].ToString() == command[1])
                            {
                                object name = row["name"];
                                object map = row["map"];
                                object count_capacity = row["count_capacity"];
                                object count_connect = row["count_connect"];
                                object range_up = row["range_up"];
                                object range_down = row["range_down"];
                                row.Close();
                                if (Convert.ToInt32(command[3].ToString()) > Convert.ToInt32(range_up.ToString()) || Convert.ToInt32(command[3].ToString()) < Convert.ToInt32(range_down.ToString()))
                                    return "Недоступен по уровню";
                                if (Convert.ToInt32(count_connect.ToString()) + 1 > Convert.ToInt32(count_capacity.ToString()))
                                    return "Комната заполнена";
                                else
                                {
                                    cmd = new SqlCommand("UPDATE [Rooms] SET count_connect = " + (Convert.ToInt32(count_connect.ToString()) + 1).ToString() + " WHERE Name = '" + command[1] + "'", connectionUser);
                                    cmd.ExecuteNonQuery();
                                    message = (Convert.ToInt32(count_connect.ToString()) + 1).ToString();
                                }
                                cmd = new SqlCommand("SELECT * FROM sys.objects WHERE type in (N'U')", connectionUser);
                                row = cmd.ExecuteReader();
                                if (row.HasRows)
                                {
                                    while (row.Read())
                                    {
                                        if (row.GetString(0) == name.ToString())
                                        {
                                            row.Close();
                                            cmd = new SqlCommand("INSERT INTO " + name.ToString() + " ([Login], [Level_user], [Damage], [Coordx], [Coordy], [Run], [Status], [Readiness]) VALUES (@login, @level_user, @damage, @coordx, @coordy, @run, @status, @readiness)", connectionUser);
                                            cmd.Parameters.Add("@login", command[2]);
                                            cmd.Parameters.Add("@level_user", command[3]);
                                            cmd.Parameters.Add("@damage", "0");
                                            cmd.Parameters.Add("@coordx", "0");
                                            cmd.Parameters.Add("@coordy", "0");
                                            cmd.Parameters.Add("@run", "0");
                                            cmd.Parameters.Add("@status", "1");
                                            cmd.Parameters.Add("@readiness", "1");
                                            cmd.ExecuteNonQuery();
                                            connectionUser.Close();
                                            message = "Вы зашли в комнату;" + message;
                                            return message;
                                        }
                                    }
                                }
                                row.Close();
                                connectionUser.Close();
                                break;
                            }
                        }
                    }
                    row.Close();
                    connectionUser.Close();
                    return "Ошибка: Case(3)";
                case "4": // Проверка на готовность и подключение игроков к игре
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [" + command[1] + "] WHERE Login = '" + command[2] + "'", connectionUser);
                    row = cmd.ExecuteReader();
                    if (row.HasRows) // если есть данные
                    {
                        if (command[3] == "ГОТОВ")
                            command[3] = "0";
                        else command[3] = "1";
                        row.Close();
                        cmd = new SqlCommand("UPDATE [" + command[1] + "] SET readiness = " + command[3] + " WHERE Login = '" + command[2] + "'", connectionUser);
                        cmd.ExecuteNonQuery();
                        connectionUser.Close();
                        return command[3];
                    }
                    row.Close();
                    connectionUser.Close();
                    return "Ошибка: Case(4)";
                case "5": // Обновление списка комнат у клиента
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [Rooms]", connectionUser);
                    row = cmd.ExecuteReader();
                    if (row.HasRows) // если есть данные
                        while (row.Read()) // построчно считываем данные
                            message += row["name"].ToString() + " " + row["map"].ToString() + " " + row["count_capacity"].ToString() + " " + row["count_connect"].ToString() + " " + row["range_up"].ToString() + " " + row["range_down"].ToString() + " " + row["status"].ToString() + ";";
                    else
                        message = "Комнат нет";
                    row.Close();
                    connectionUser.Close();
                    return message;
                case "6": // обновление списка игроков в комнатах
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM sys.objects WHERE type in (N'U')", connectionUser);
                    row = cmd.ExecuteReader();
                    message = "";
                    if (row.HasRows) // если есть данные
                    {
                        while (row.Read()) // построчно считываем данные
                        {
                            if (row.GetString(0) == command[1])
                            {
                                row.Close();
                                cmd = new SqlCommand("SELECT * FROM [" + command[1] + "]", connectionUser);
                                row = cmd.ExecuteReader();
                                message = "";
                                if (row.HasRows) // если есть данные
                                    while (row.Read()) // построчно считываем данные
                                        message += row["login"].ToString() + " " + row["level_user"].ToString() + " " + row["status"].ToString() + " " + row["readiness"].ToString() + ";";
                                break;
                            }
                        }
                    }
                    else
                        message = "0";
                    row.Close();
                    connectionUser.Close();
                    return message;
                case "7": // выход игрока из комнаты и выход из игры
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    if (command[1] != "")
                    {
                        cmd = new SqlCommand("SELECT * FROM sys.objects WHERE type in (N'U')", connectionUser);
                        row = cmd.ExecuteReader();
                        message = "";
                        if (row.HasRows) // если есть данные
                        {
                            while (row.Read()) // построчно считываем данные
                            {
                                if (row.GetString(0) == command[1])
                                {
                                    cmd = new SqlCommand("DELETE FROM [" + row.GetString(0) + "] WHERE Login = '" + command[2] + "'", connectionUser);
                                    row.Close();
                                    cmd.ExecuteNonQuery();
                                    message += " (данные удалены из таблицы) ";
                                    cmd = new SqlCommand("SELECT * FROM [Rooms]", connectionUser);
                                    row = cmd.ExecuteReader();
                                    if (row.HasRows) // если есть данные
                                    {
                                        while (row.Read()) // построчно считываем данные
                                        {
                                            if (row["name"].ToString() == command[1])
                                            {
                                                object count_connect = Convert.ToInt32(row["count_connect"]) - 1;
                                                row.Close();
                                                if (count_connect.ToString() == "0")
                                                {
                                                    cmd = new SqlCommand("DROP TABLE " + command[1], connectionUser);
                                                    cmd.ExecuteNonQuery();
                                                    cmd = new SqlCommand("DELETE FROM [Rooms] WHERE Name = '" + command[1] + "'", connectionUser);
                                                    cmd.ExecuteNonQuery();
                                                    message += " (запись о комнате удалена) ";
                                                }
                                                else
                                                {
                                                    cmd = new SqlCommand("UPDATE [Rooms] SET count_connect = " + count_connect.ToString() + " WHERE Name = '" + command[1] + "'", connectionUser);
                                                    cmd.ExecuteNonQuery();
                                                    message += " (счетчик коннектов изменен) ";
                                                }
                                                break;
                                            }
                                        }
                                    }
                                    if (command[3] == "1" && command[2] != "Admin")
                                    {
                                        cmd = new SqlCommand("UPDATE [Users] SET status = 0 WHERE Login = '" + command[2] + "'", connectionUser);
                                        cmd.ExecuteNonQuery();
                                        message += " (онлайн - 0)";
                                    }
                                    break;
                                }
                            }
                        }
                        row.Close();
                    }
                    else if (command[2] != "Admin")
                    {
                        cmd = new SqlCommand("UPDATE [Users] SET status = 0 WHERE Login = '" + command[2] + "'", connectionUser);
                        cmd.ExecuteNonQuery();
                        message += " (онлайн - 0)";
                    }
                    connectionUser.Close();
                    return message;
                case "8": // подсчет кол-во онлайн игроков
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT COUNT(*) as count FROM Users WHERE status = 1", connectionUser);
                    message = cmd.ExecuteScalar().ToString();
                    connectionUser.Close();
                    return message;
                case "9": // *операция удалена - заменить*
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT COUNT(*) as count FROM " + command[1] + " WHERE readiness = 1", connectionUser);
                    message = cmd.ExecuteScalar().ToString();
                    cmd = new SqlCommand("SELECT * FROM [Rooms] WHERE Name = '" + command[1] + "'", connectionUser);
                    row = cmd.ExecuteReader();
                    if (row.HasRows)
                        while (row.Read())
                            if (Convert.ToInt32(row["count_capacity"]) == Convert.ToInt32(message))
                                return "вход";
                            else
                            {
                                message = "нет";
                                break;
                            }
                    row.Close();
                    connectionUser.Close();
                    return message;
                case "10": // задание стартовой позиции игроков
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT COUNT(*) as count FROM " + command[1] + " WHERE coordx = " + command[2] + " AND coordy = " + command[3], connectionUser);
                    if (Convert.ToInt32(cmd.ExecuteScalar()) != 0)
                        return "0";
                    cmd = new SqlCommand("UPDATE [" + command[1] + "] SET coordx = " + command[2] + ", coordy = " + command[3] + " WHERE Login = '" + command[4] + "'", connectionUser);
                    cmd.ExecuteNonQuery();
                    connectionUser.Close();
                    return "отправить";
                case "11": // обработка кто первый ходит при подключении
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT COUNT(*) as count FROM " + command[1] + " WHERE readiness = 1", connectionUser);
                    int countSize = Convert.ToInt32(cmd.ExecuteScalar());
                    cmd = new SqlCommand("SELECT * FROM [Rooms]", connectionUser);
                    row = cmd.ExecuteReader();
                    while (row.Read()) // построчно считываем данные
                    {
                        if(row["name"].ToString() == command[1])
                        {
                            if(Convert.ToInt32(row["count_capacity"].ToString()) == countSize)
                            {
                                row.Close();
                                cmd = new SqlCommand("SELECT * FROM [" + command[1] + "]", connectionUser);
                                row = cmd.ExecuteReader();
                                int counts = 0;
                                if (row.HasRows) // если есть данные
                                {
                                    while (row.Read()) // построчно считываем данные
                                    {
                                        if (counts != -1)
                                        {
                                            if (Convert.ToInt32(row["run"].ToString()) == 0)
                                                counts++;
                                            else if (Convert.ToInt32(row["run"].ToString()) == -1)
                                                counts = -1;
                                        }
                                        else
                                        {
                                            cmd = new SqlCommand("UPDATE [" + command[1] + "] SET run = 0 WHERE Login = '" + row["login"].ToString() + "'", connectionUser);
                                            cmd.ExecuteNonQuery();
                                            row.Close();
                                            cmd = new SqlCommand("SELECT * FROM [" + command[1] + "]", connectionUser);
                                            row = cmd.ExecuteReader();
                                            int i = 0;
                                            while (row.Read()) // построчно считываем данные
                                            {
                                                if (i == counts)
                                                {
                                                    cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = 1 WHERE Login = '" + row["login"].ToString() + "'", connectionUser);
                                                    cmd.ExecuteNonQuery();
                                                    message = "t+" + " " + row["login"].ToString();
                                                    break;
                                                }
                                                i++;
                                            }
                                            row.Close();
                                            break;
                                        }
                                    }
                                }
                                if (countSize == counts)
                                {
                                    row.Close();
                                    cmd = new SqlCommand("SELECT * FROM [" + command[1] + "]", connectionUser);
                                    row = cmd.ExecuteReader();
                                    row.Read();
                                    string nickActive = row["login"].ToString();
                                    row.Close();
                                    cmd = new SqlCommand("UPDATE [" + command[1] + "] SET run = 1 WHERE Login = '" + nickActive + "'", connectionUser);
                                    cmd.ExecuteNonQuery();
                                    message = "t+" + " " + nickActive;
                                }
                                connectionUser.Close();
                                return message;
                            }
                            else
                                message = "none";
                            break;
                        }
                    }
                    row.Close();
                    connectionUser.Close();
                    return message;
                case "13": // задание стартовой позиции игроков
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT * FROM [" + command[1] + "]", connectionUser);
                    row = cmd.ExecuteReader();
                    message = "";
                    if (row.HasRows) // если есть данные
                        while (row.Read()) // построчно считываем данные
                            message += row["login"].ToString() + " " + row["level_user"].ToString() + " " + row["damage"].ToString() + " " + row["coordx"].ToString() + " " + row["coordy"].ToString() + " " + row["run"].ToString() + " " + row["status"].ToString() + " " + row["readiness"].ToString() + ";";
                    connectionUser.Close();
                    return "отправить";
                case "14": // смерть игрока
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    if (command[1] == "d")
                        cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = -2 WHERE Login = '" + command[3] + "'", connectionUser);
                    else
                        cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = -1 WHERE Login = '" + command[3] + "'", connectionUser);
                    cmd.ExecuteNonQuery();
                    connectionUser.Close();
                    return "отправить";
                case "15": // смерть игрока
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    if (command[1] == "d")
                        cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = -2 WHERE Login = '" + command[3] + "'", connectionUser);
                    else
                        cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = -1 WHERE Login = '" + command[3] + "'", connectionUser);
                    cmd.ExecuteNonQuery();
                    connectionUser.Close();
                    return "отправить";
                case "16": // выборка того, кто ходит
                    connectionUser = new SqlConnection(connectionString);
                    connectionUser.Open();
                    cmd = new SqlCommand("SELECT COUNT(*) as count FROM " + command[2] + " WHERE readiness = 1", connectionUser);
                    countSize = Convert.ToInt32(cmd.ExecuteScalar().ToString());
                    cmd = new SqlCommand("SELECT * FROM [" + command[2] + "]", connectionUser);
                    row = cmd.ExecuteReader();
                    string changeuser = "";
                    int count = 0;
                    int countTwo = 0;
                    if (row.HasRows) // если есть данные
                    {
                        while (row.Read()) // построчно считываем данные
                        {
                            if (countTwo != -1)
                            {
                                if (Convert.ToInt32(row["run"].ToString()) == 0)
                                    count++;
                                else if (Convert.ToInt32(row["run"].ToString()) == -1)
                                {
                                    countTwo = -1;
                                    count++;
                                    changeuser = row["login"].ToString();
                                }
                            }
                            else
                            {
                                row.Close();
                                cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = 0 WHERE Login = '" + changeuser + "'", connectionUser);
                                cmd.ExecuteNonQuery();
                                cmd = new SqlCommand("SELECT * FROM [" + command[2] + "]", connectionUser);
                                row = cmd.ExecuteReader();
                                int i = 0;
                                while (row.Read()) // построчно считываем данные
                                {
                                    if (i == count)
                                    {
                                        changeuser = row["login"].ToString();
                                        row.Close();
                                        cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = 1 WHERE Login = '" + changeuser + "'", connectionUser);
                                        cmd.ExecuteNonQuery();
                                        message = "t+" + " " + changeuser;
                                        break;
                                    }
                                    i++;
                                }
                                row.Close();
                                break;
                            }
                        }
                    }
                    if (countSize == count)
                    {
                        row.Close();
                        if(countTwo == -1)
                        {
                            cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = 0 WHERE Login = '" + changeuser + "'", connectionUser);
                            cmd.ExecuteNonQuery();
                        }
                        cmd = new SqlCommand("SELECT * FROM [" + command[2] + "]", connectionUser);
                        row = cmd.ExecuteReader();
                        row.Read();
                        cmd = new SqlCommand("UPDATE [" + command[2] + "] SET run = 1 WHERE Login = '" + row["login"].ToString() + "'", connectionUser);
                        message = "t+" + " " + row["login"].ToString();
                        row.Close();
                        cmd.ExecuteNonQuery();
                    }
                    connectionUser.Close();
                    return message;
                default:
                    return "Ошибка";
            }
        }
    }
}