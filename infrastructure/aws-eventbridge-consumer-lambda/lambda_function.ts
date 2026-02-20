const WEBHOOK_URL = process.env.WEBHOOK_URL;
  const CONNECT_API_KEY = process.env.CONNECT_API_KEY; // optional

  export const handler = async (event) => {
    try {
      const res = await fetch(WEBHOOK_URL, {
        method: "POST",
        headers: {
          "content-type": "application/json",
          ...(CONNECT_API_KEY ? { "x-api-key": CONNECT_API_KEY } : {}),
        },
        body: JSON.stringify(event),
      });

      const text = await res.text();
      console.log("Forwarded event:", {
        status: res.status,
        ok: res.ok,
        body: text,
      });

      return {
        statusCode: res.ok ? 200 : 502,
        body: JSON.stringify({ ok: res.ok, status: res.status, response: text }),
      };
    } catch (err) {
      console.error("Failed to forward event", err);
      return {
        statusCode: 500,
        body: JSON.stringify({ ok: false, error: String(err) }),
      };
    }
  };


