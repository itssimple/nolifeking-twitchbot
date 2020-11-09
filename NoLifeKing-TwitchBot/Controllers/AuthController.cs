using Microsoft.AspNetCore.Mvc;

namespace NoLifeKing_TwitchBot.Controllers
{
    public class AuthController : Controller
    {
        [HttpGet("/twitch_auth")]
        public IActionResult Index()
        {
            Program.VerificationCode = Request.Query["code"];
            Program.VerificationState = Request.Query["state"];

            return Content($@"<html>
<head>
<title>NoLifeKing85 bot auth</title>
</head>
<body>
Authentication is done, this window will close itself in 5 seconds (or so)
<script type=""text/javascript"">
setTimeout(function() {{ window.close(); }}, 5000);
</script>
</body>
</html>", "text/html");
        }
    }
}
