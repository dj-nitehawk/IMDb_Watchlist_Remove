' DESCRIPTION:
' ============
' This script can be used to programatically remove a movie from your personal imdb watchlist.
' Usually used with automation apps like flexget. Just pass in the imdbid of a movie.
'
' For this to work, you must link your facebook a/c with your imdb a/c. The reason being, imdb has
' introduced a login captcha and direct login via scripts is now impossible. The solution was to 
' login to imdb using facebook oauth. So log in to imdb using your facebook a/c at least once and
' set the login credentials below, build the app and then upload it to your webserver capable of 
' running asp.net 3.5 and above apps.
'
' To use, pass in an imdbid of a watchlisted movie like so:
' http://your_server.com/app_path/?movie=tt1234567
'
' Note: This script saves and reuses cookies so facebook login will only happen when cookies expire.
' Also it is advisable to use a fake/secondary fb a/c for this purpose instead of your primary a/c.

Imports System.Net
Imports System.IO
Imports Newtonsoft.Json
Imports System.Runtime.Serialization.Formatters.Binary

Public Class WebResponse
    Property Content As String
    Property StatusCode As HttpStatusCode
End Class

Public Class _Default
    Inherits System.Web.UI.Page

    'CHANGE THE XXX VALUES BELOW TO YOUR PERSONAL INFO

    'your first & last name seperated by a space in the middle. 
    'must match with what you have set in https://secure.imdb.com/register-imdb/details
    'this string is used to determine whether logging in was successful or not.
    Dim IMDBUserString As String = "XXX XXX"

    'your facebook login username. usually your email address. 
    'facebook oauth is used to log in to imdb as direct login does not work due to imdb captcha.
    Dim FBUserName As String = "XXX@XXX.XXX"

    'your facebook login password.
    Dim FBPassword As String = "XXX"

    'your pushbullet api key from https://www.pushbullet.com/account
    'if you don't want notifications for errors, just leave as is.
    Dim PushBulletAPIKey As String = "XXX"

    'DO NOT MODIFY BELOW HERE UNLESS YOU KNOW WHAT YOU ARE DOING

    Dim PushBulletAPI As String = "https://api.pushbullet.com/v2/pushes"
    Shared CookieFile As String = My.Request.MapPath("~/cookiejar.txt")
    Shared CookieJar As New Net.CookieContainer()

    Protected Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        If Not String.IsNullOrEmpty(Request.QueryString("movie")) Then
            Dim movieID As String = Request.QueryString("movie")
            RemoveMovie(movieID)
        Else
            Response.Write("Need an imdb id to work with...")
        End If

    End Sub

    Private Shared Sub SaveCookiesToFile()
        If Not CookieJar.Count = 0 Then
            Using stream As Stream = File.Create(CookieFile)
                Try
                    Dim formatter As New BinaryFormatter()
                    formatter.Serialize(stream, CookieJar)
                Catch e As Exception
                    My.Response.Write("Problem saving cookies to disk!</br></br>")
                End Try
            End Using
        End If
    End Sub

    Private Shared Sub RestoreCookiesFromFile()
        Try
            Using stream As Stream = File.Open(CookieFile, FileMode.Open, FileAccess.Read)
                Dim formatter As New BinaryFormatter()
                CookieJar = DirectCast(formatter.Deserialize(stream), CookieContainer)
            End Using
        Catch e As Exception
            My.Response.Write("Problem reading cookies from disk!</br></br>")
        End Try
    End Sub

    Private Function LoginToIMDBWithFB() As WebResponse
        Response.Write("Logging in to imdb via facebook...</br></br>")
        RequestPage("https://graph.facebook.com/oauth/authorize?client_id=127059960673829&redirect_uri=https%3A%2F%2Fsecure.imdb.com%2Foauth%2Fm_login", False, "") ' get login page
        Dim resp As WebResponse = RequestPage("https://www.facebook.com/login.php?login_attempt=1&next=https%3A%2F%2Fwww.facebook.com%2Fv1.0%2Fdialog%2Foauth%3Fredirect_uri%3Dhttps%253A%252F%252Fsecure.imdb.com%252Foauth%252Fm_login%26client_id%3D127059960673829%26ret%3Dlogin", True, "email=" + FBUserName + "&pass=" + FBPassword + "&api_key=127059960673829&persistent=1&default_persistent=0&display=page&legacy_return=1&skip_api_login=1&signed_next=1&trynum=1&login=Log In") ' post to login form
        Return resp
    End Function

    Private Sub RemoveMovie(ImdbID As String)

        Dim resp As WebResponse

        If File.Exists(CookieFile) Then
            RestoreCookiesFromFile()
        End If

        resp = RequestPage("http://m.imdb.com/list/watchlist", False, "")

        If Not resp.Content.Contains(IMDBUserString) Then
            resp = LoginToIMDBWithFB()
        End If

        If resp.Content.Contains(IMDBUserString) Then

            Dim movieResp As WebResponse = RequestPage("http://www.imdb.com/list/_ajax/watchlist_has", True, "consts[]=" + ImdbID + "&tracking_tag=wlb-lite") ' get json string from movie page
            Dim itmID As String
            Dim lstID As String

            Try
                itmID = JsonConvert.DeserializeObject(movieResp.Content)("has")(ImdbID)(0).ToString 'extract watchlist itemid from json
                lstID = JsonConvert.DeserializeObject(movieResp.Content)("list_id").ToString 'extract listid from json
            Catch ex As Exception
                itmID = Nothing
                lstID = Nothing
            End Try

            If Not String.IsNullOrEmpty(itmID) And Not String.IsNullOrEmpty(lstID) Then

                Dim delResp As WebResponse = RequestPage("http://www.imdb.com/list/_ajax/edit", True, "action=delete&list_id=" + lstID + "&list_item_id=" + itmID + "&ref_tag=title&list_class=WATCHLIST")

                If delResp.StatusCode = HttpStatusCode.OK Then
                    Response.Write("SUCCESS!!!")
                Else
                    ReportError("COULDN'T REMOVE THE MOVIE (" + ImdbID + ") FROM WATCHLIST!!!")
                End If

            Else
                ReportError("THIS MOVIE (" + ImdbID + ") IS NOT WATCHLISTED!!!")
            End If

            SaveCookiesToFile()

        Else
            ReportError("FAILED TO LOGIN!!!")
        End If
    End Sub

    Private Sub ReportError(Message As String)

        Dim data As String = "type=note&title=Imdb Error&body=" + Message
        My.Response.Write(Message + "</br></br>")

        If Not String.IsNullOrEmpty(PushBulletAPIKey) And Not PushBulletAPIKey = "XXX" Then

            If RequestPage(PushBulletAPI, True, data).StatusCode = HttpStatusCode.OK Then
                My.Response.Write("Owner Notified Of Error.")
            Else
                My.Response.Write("Owner could not be notified :-(")
            End If

        End If

    End Sub

    Private Function RequestPage(URL As String, POST As Boolean, FORMData As String) As WebResponse

        Dim req As HttpWebRequest = Net.HttpWebRequest.Create(URL)
        Dim res As Net.HttpWebResponse
        Dim wRes As New WebResponse

        If URL = PushBulletAPI Then
            req.Headers(HttpRequestHeader.Authorization) = String.Format("Basic {0}", Convert.ToBase64String(Encoding.UTF8.GetBytes(PushBulletAPIKey + ":")))
        End If

        req.UserAgent = "Mozilla/5.0 (Windows NT 5.1; rv:9.0.1) Gecko/20100101 Firefox/9.0.1"
        req.Accept = "*/*"
        req.CookieContainer = CookieJar
        req.KeepAlive = True
        req.AllowAutoRedirect = True
        If POST Then
            req.Method = "POST"
            req.ContentType = "application/x-www-form-urlencoded"
            req.ContentLength = FORMData.Length
            Dim reqStream As Stream = req.GetRequestStream()
            Dim postBytes As Byte() = Encoding.UTF8.GetBytes(FORMData)
            reqStream.Write(postBytes, 0, postBytes.Length)
            reqStream.Close()
        Else
            req.Method = "GET"
        End If
        res = req.GetResponse()
        wRes.StatusCode = res.StatusCode
        CookieJar.Add(res.Cookies)
        Dim stream As StreamReader = New StreamReader(res.GetResponseStream())
        wRes.Content = stream.ReadToEnd()
        res.Close()
        stream.Close()
        req = Nothing
        Return wRes
    End Function

End Class