import axios from "axios";
import Cookies from "js-cookie";

const apiClient = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL ?? "https://localhost:7001/api",
  headers: {
    "Content-Type": "application/json",
  },
});

apiClient.interceptors.request.use(
  (config) => {
    // js-cookie yalnızca tarayıcıda çalışır; SSR sırasında atla
    if (typeof window === "undefined") return config;

    const token =
      Cookies.get("token") ?? localStorage.getItem("token") ?? null;

    if (token) {
      config.headers.set("Authorization", `Bearer ${token}`);
    }

    return config;
  },
  (error) => Promise.reject(error),
);

export default apiClient;
