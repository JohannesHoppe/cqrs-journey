// ==============================================================================================================
// Microsoft patterns & practices
// CQRS Journey project
// ==============================================================================================================
// ©2012 Microsoft. All rights reserved. Certain content used with permission from contributors
// http://go.microsoft.com/fwlink/p/?LinkID=258575
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance 
// with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is 
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and limitations under the License.
// ==============================================================================================================

using System;
using System.Data;
using System.Web.Mvc;
using AutoMapper;
using Infrastructure.Utils;

namespace Conference.Web.Admin.Controllers
{
    public class ConferenceController : Controller
    {
        private ConferenceService service;

        private ConferenceService Service {
            get { return service ?? (service = new ConferenceService(MvcApplication.EventBus)); }
        }

        public ConferenceInfo Conference { get; private set; }

        static ConferenceController()
        {
            Mapper.CreateMap<EditableConferenceInfo, ConferenceInfo>();
        }

        // TODO: Locate and Create are the ONLY methods that don't require authentication/location info.

        /// <summary>
        ///     We receive the slug value as a kind of cross-cutting value that
        ///     all methods need and use, so we catch and load the conference here,
        ///     so it's available for all. Each method doesn't need the slug parameter.
        /// </summary>
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var slug = (string) ControllerContext.RequestContext.RouteData.Values["slug"];
            if (!string.IsNullOrEmpty(slug)) {
                ViewBag.Slug = slug;
                Conference = Service.FindConference(slug);

                if (Conference != null) {
                    // check access
                    var accessCode = (string) ControllerContext.RequestContext.RouteData.Values["accessCode"];

                    if (accessCode == null || !string.Equals(accessCode, Conference.AccessCode, StringComparison.Ordinal)) {
                        filterContext.Result = new HttpUnauthorizedResult("Invalid access code.");
                    } else {
                        ViewBag.OwnerName = Conference.OwnerName;
                        ViewBag.WasEverPublished = Conference.WasEverPublished;
                    }
                }
            }

            base.OnActionExecuting(filterContext);
        }

        #region Orders

        public ViewResult Orders()
        {
            var orders = Service.FindOrders(Conference.Id);

            return View(orders);
        }

        #endregion

        #region Conference Details

        public ActionResult Locate()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Locate(string email, string accessCode)
        {
            var conference = Service.FindConference(email, accessCode);
            if (conference == null) {
                ModelState.AddModelError(string.Empty, "Could not locate a conference with the provided email and access code.");
                // Preserve input so the user doesn't have to type email again.
                ViewBag.Email = email;

                return View();
            }

            // TODO: This is not very secure. Should use a better authorization infrastructure in a real production system.
            return RedirectToAction("Index", new {slug = conference.Slug, accessCode});
        }

        public ActionResult Index()
        {
            if (Conference == null) {
                return HttpNotFound();
            }
            return View(Conference);
        }

        public ActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Create([Bind(Exclude = "Id,AccessCode,Seats,WasEverPublished")] ConferenceInfo conference)
        {
            if (ModelState.IsValid) {
                try {
                    conference.Id = GuidUtil.NewSequentialId();
                    Service.CreateConference(conference);
                } catch (DuplicateNameException e) {
                    ModelState.AddModelError("Slug", e.Message);
                    return View(conference);
                }

                return RedirectToAction("Index", new {slug = conference.Slug, accessCode = conference.AccessCode});
            }

            return View(conference);
        }

        public ActionResult Edit()
        {
            if (Conference == null) {
                return HttpNotFound();
            }
            return View(Conference);
        }

        [HttpPost]
        public ActionResult Edit(EditableConferenceInfo conference)
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            if (ModelState.IsValid) {
                var edited = Mapper.Map(conference, Conference);
                Service.UpdateConference(edited);
                return RedirectToAction("Index", new {slug = edited.Slug, accessCode = edited.AccessCode});
            }

            return View(Conference);
        }

        [HttpPost]
        public ActionResult Publish()
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            Service.Publish(Conference.Id);

            return RedirectToAction("Index", new {slug = Conference.Slug, accessCode = Conference.AccessCode});
        }

        [HttpPost]
        public ActionResult Unpublish()
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            Service.Unpublish(Conference.Id);

            return RedirectToAction("Index", new {slug = Conference.Slug, accessCode = Conference.AccessCode});
        }

        #endregion

        #region Seat Types

        public ViewResult Seats()
        {
            return View();
        }

        public ActionResult SeatGrid()
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            return PartialView(Service.FindSeatTypes(Conference.Id));
        }

        public ActionResult SeatRow(Guid id)
        {
            return PartialView("SeatGrid", new[] {Service.FindSeatType(id)});
        }

        public ActionResult CreateSeat()
        {
            return PartialView("EditSeat");
        }

        [HttpPost]
        public ActionResult CreateSeat(SeatType seat)
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            if (ModelState.IsValid) {
                seat.Id = GuidUtil.NewSequentialId();
                Service.CreateSeat(Conference.Id, seat);

                return PartialView("SeatGrid", new[] {seat});
            }

            return PartialView("EditSeat", seat);
        }

        public ActionResult EditSeat(Guid id)
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            return PartialView(Service.FindSeatType(id));
        }

        [HttpPost]
        public ActionResult EditSeat(SeatType seat)
        {
            if (Conference == null) {
                return HttpNotFound();
            }

            if (ModelState.IsValid) {
                try {
                    Service.UpdateSeat(Conference.Id, seat);
                } catch (ObjectNotFoundException) {
                    return HttpNotFound();
                }

                return PartialView("SeatGrid", new[] {seat});
            }

            return PartialView(seat);
        }

        [HttpPost]
        public void DeleteSeat(Guid id)
        {
            Service.DeleteSeat(id);
        }

        #endregion
    }
}