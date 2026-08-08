import { createRouter, createWebHashHistory } from "vue-router";

import HomeView from "./views/HomeView.vue";
import GuideHostView from "./views/GuideHostView.vue";
const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),

  routes: [
    {
      path: "/",
      name: "home",
      component: HomeView,
    },
    {
      path: "/setup/host",
      name: "setup-host",
      component: GuideHostView,
    },
  ],
});

export default router;
