# What's new in Job Tracker

One section per version, newest first. Written for the people who use the app: they are shown inside the program (Settings, the update bar,
and once after each update) and become the notes on the GitHub release. `tools/publish.ps1` refuses to build a version that has no section here.

## 1.4.1

- **A warning when you paste the same job twice.** A company can have many roles, so a new job at the same company saves as usual. But if the job description is 70% or more the same as one you already saved for that company, you are asked first. A window shows both descriptions side by side with the lines that are only in one of them highlighted. Go back (nothing you typed is lost), save it anyway if it is a different role, or open the one you already have.
- **Remove an application you added by mistake.** The new **Remove** button asks first, then moves the application's folder to the Windows Recycle Bin, so you can get it back from there. Bullets you starred from it stay in Starred bullets.
- **A Word file ready to upload or attach.** The file is now named like `Priya_Raman_Resume.docx` (first and last name, no spaces, no company name) and is saved in the application's folder, so you can select it and press Ctrl+C. A new **Copy file** button does the same from the preview window.
- **Your resume now, at the top of the saved versions.** The Match tab shows the resume in use first, with its score and Word file name. Every saved version shows the score it would give, so you can see an improvement before you choose it.
- **Add new bullets is now the first tab of Improve bullets**, and it lists the keywords from the job description that are not in your resume yet. Tick the ones you really used, or just type a skill such as Kubernetes: the bullet ties it to work that job already describes and adds nothing else, and the window reminds you to check it is true before you add it.
- **Improve bullets no longer fails with "could not read".** If the AI answers in words instead of bullets, you now see what it said. A bar at the bottom of the window shows how long it has been waiting and has a **Stop** button.
- The buttons under an application's title no longer touch when the window is narrow, and neither does the search bar.
## 1.4.0

- **Tailor your resume to a job.** Every application has a new **Match** tab. It lists the skills the job description asks for and shows which your resume already says, with a percentage counted from the two texts. For a skill you have really used but never wrote down, click **I've used it** and say where.
- **Improve bullets.** Reword one bullet, reword all the bullets of a job together, or add new bullets to a job, in the job description's words, with a word-by-word before and after. Anything that adds a number, tool, role or name you did not write is thrown out before you see it, and the window says why. Your resume is never overwritten: changes are saved as a new version.
- **A Word document of the result.** Preview the whole page, open its folder, open it in Word, or download it. If you attached a Word resume, the file is a copy of it with only your changes made in it, so your fonts and layout stay. Otherwise you get a clean one-column document that applicant tracking systems read reliably. Every file is read back and checked against your text.
- **Build a resume from scratch.** The new **My resume** button walks you through contact details, a summary, your jobs, education and skills, saves as you type, and shows tips beside weak bullets. Bullets can be typed, put together from parts, or written by an AI helper from your own answers only. Use it in **New application** with one click.
- **Pay from the job description.** The salary, range or hourly, daily or monthly rate a posting states is shown next to the date and in the list, for full-time and contract roles. If the posting states none, it says Not available. Revenue, funding and bonuses are never mistaken for pay.
- **Attach your resume as a file** (PDF, Word, Markdown or text) when adding an application, or drop it on the resume box.
- **AI help is optional and yours.** Add your own OpenRouter or Anthropic key in Settings (it is kept in Windows Credential Manager, not in a file) and pick a model from a list the service provides. You are asked before anything is sent, and only the bullet you picked, the job description and your notes go.
- Changing where your applications are saved now carries your built resume along too.

## 1.3.1

- Moving your applications (for example into OneDrive) now fully tidies up the old folders. OneDrive marks folders read-only, which used to leave empty folders behind.
- After an update, What's new shows everything new since you last looked, so a small fix does not hide the bigger news before it.

## 1.3.0

- See what changed right inside Job Tracker: **What's new** is on the update bar and in Settings, and it opens by itself once after an update.
- Settings now tells you whether your applications are backed up by OneDrive, and moves them there in one click if they are not.
- A new install keeps your applications in Documents when OneDrive syncs it, so they survive a damaged profile or a Windows reset.
- After a reinstall, Job Tracker finds your applications again by itself once OneDrive has restored them.

## 1.2.0

- Full-time and Contract tabs above your applications, each with a count, to keep the two kinds apart.
- Choose Full-time or Contract when you add an application. It is filled in for you when the posting says contract.
- Change an application's type later and it moves to the other tab.

## 1.1.0

- Choose where your applications are saved: while installing, and any time in Settings.
- Moving the folder moves your saved applications too. It copies and checks everything first, so nothing is lost.

## 1.0.0

- Paste a job page and your resume: they are saved in a folder for the company and date.
- Company, role and link are filled in for you, and menus and buttons are cleaned out of the job description.
- Search the job descriptions and resumes, star the lines you want to reuse, and copy them into your next resume.
- Installs with one click and updates itself.
