using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using ApiPMU.Models; // Assurez-vous que le namespace correspond à celui de votre DbContext et de vos entités

namespace ApiPMU.Services
{
    /// <summary>
    /// Service d'envoi d'email pour notifier la fin du téléchargement des données.
    /// </summary>
    public static class EmailService
    {
        /// <summary>
        /// Envoie un courriel de récapitulatif pour la date spécifiée.
        /// </summary>
        /// <param name="dateProno">La date ciblée (pour laquelle envoyer le récapitulatif).</param>
        /// <param name="flagTRT">Si vrai, ajoute un avertissement dans le sujet.</param>
        /// <param name="subjectPrefix">Le préfixe du sujet du courriel.</param>
        /// <param name="log">Le log de traitement à inclure dans le corps du courriel.</param>
        /// <param name="serveur">Le préfixe du serveur (utilisé pour former l'adresse expéditeur).</param>
        /// <param name="dbContext">Le DbContext pour accéder aux tables Reunions et Courses.</param>
        public static async Task SendCompletionEmailAsync(DateTime dateProno, bool flagTRT, string subjectPrefix, string log, string serveur, ApiPMUDbContext dbContext)
        {
            int retry = 1;
            while (true)
            {
                try
                {
                    // Création du message email
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress("", serveur + ".genturf@free.fr"));
                    message.To.Add(new MailboxAddress("", "tchicken@gmail.com"));

                    // Construction du sujet
                    message.Subject = subjectPrefix + " " + DateTime.Now.ToString("T");
                    if (flagTRT)
                    {
                        message.Subject += " *** Incident ***";
                    }

                    // Récupération des réunions pour la date spécifiée
                    var reunions = await dbContext.Reunions
                        .Where(r => EF.Functions.DateDiffDay(r.DateReunion, dateProno) == 0)
                        .OrderBy(r => r.NumReunion)
                        .ToListAsync();

                    int nbR = 0;  // Nombre de réunions
                    int nbC = 0;  // Nombre total de courses
                    string corps = "";

                    // Parcours des réunions pour construire le corps du message
                    foreach (var reunion in reunions)
                    {
                        // Récupération des informations de la réunion
                        string numGeny = reunion.NumGeny;
                        string reunionStr = "R" + reunion.NumReunion.ToString();
                        string lieuCourse = (reunion.LieuCourse ?? "").ToLower();

                        // Récupération des courses associées à cette réunion via NumGeny
                        var courses = await dbContext.Courses
                            .Where(c => c.NumGeny == numGeny)
                            .ToListAsync();

                        string depart = "";
                        if (courses.Any())
                        {
                            // On récupère le libellé de la première course et on extrait une sous-chaîne
                            string myLib = courses.First().Libelle;
                            if (!string.IsNullOrEmpty(myLib) && myLib.Length >= 12)
                            {
                                // En VB, Mid$(MyLib, 8, 5) correspond à Substring(7, 5) en C#
                                depart = myLib.Substring(7, 5);
                            }
                            nbC += courses.Count;
                        }

                        // Concaténation d'une ligne dans le corps du message pour cette réunion
                        corps += $"<font size='4'>{depart}&nbsp;<font color='blue'>{reunionStr}-{lieuCourse}</font>";
                        corps += $"<font size='4' color='red'> : {courses.Count}</font><font size='4'> courses.</font><br>";
                        nbR++;
                    }

                    // Construction de l'en-tête HTML
                    string tete = $"<html><body><h3><b><font color='green'>{DateTime.Now}</font></b></h3>";
                    tete += $"<h3><b><font color='blue'>{dateProno.ToString("D").ToUpper()}&nbsp;&nbsp; : &nbsp;&nbsp;</font>";
                    tete += $"<font color='red'>{nbR}-</font>Réunions&nbsp;&nbsp;<font color='red'>{nbC}-</font>Courses</h3>{corps}</b>";
                    tete += $"<br><br><h3><b><font color='green'>Trace log traitement :</font></b></h3>{log}";
                    string htmlBody = tete + "</body></html>";

                    // Construction du corps du message avec BodyBuilder
                    var builder = new BodyBuilder { HtmlBody = htmlBody };
                    message.Body = builder.ToMessageBody();

                    // Envoi du courriel via SMTP avec MailKit
                    using (var smtp = new SmtpClient())
                    {
                        await smtp.ConnectAsync("smtp.free.fr", 465, SecureSocketOptions.Auto);
                        await smtp.AuthenticateAsync(serveur + ".genturf@free.fr", "Laurence#1968#");
                        await smtp.SendAsync(message);
                        await smtp.DisconnectAsync(true);
                    }

                    // Envoi réussi, sortir de la boucle
                    break;
                }
                catch (Exception ex) when (retry < 3)
                {
                    // En cas d'échec, consigner l'erreur dans le log et retenter après une minute
                    Console.WriteLine("Courriel - Echec de l'envoi " + retry);
                    // Vous pouvez également ajouter à une variable de log, par exemple :
                    log += $"<br> - Courriel - Echec de l'envoi {retry} : {ex.Message}.";
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Courriel - L'envoi a échoué " + serveur + ".genturf@free.fr");
                    log += $"<br> - Courriel - L'envoi a échoué {serveur}.genturf@free.fr : {ex.Message}";
                    break;
                }

                // Pause d'une minute avant de retenter
                await Task.Delay(TimeSpan.FromMinutes(1));
                retry++;
            }
        }
    }
}
