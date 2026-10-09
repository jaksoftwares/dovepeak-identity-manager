<#--
  Branded layout for every HTML email (verification, password reset, security alerts). Tenant values come from
  realm localization texts written by the Management API, which validates them; they are re-checked here.
-->
<#macro emailLayout>
<#assign dpPrimary = msg("dovepeakBrandPrimaryColor")?matches("#[0-9A-Fa-f]{6}")?then(msg("dovepeakBrandPrimaryColor"), "#ff6300")>
<#assign dpLogo = msg("dovepeakBrandLogoUrl")>
<html lang="${locale.language}" dir="${(ltr)?then('ltr','rtl')}">
<body style="margin:0;padding:0;background:#f4f4fa;font-family:'Segoe UI',Arial,sans-serif;color:#14142b;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4fa;padding:24px 0;">
    <tr><td align="center">
      <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:#ffffff;border-radius:10px;overflow:hidden;border-top:5px solid ${dpPrimary};">
        <tr><td style="padding:28px 32px 8px;">
          <#if dpLogo?starts_with("https://")>
          <img src="${dpLogo}" alt="" style="max-height:48px;max-width:200px;display:block;margin-bottom:12px;" />
          </#if>
          <div style="font-size:20px;font-weight:600;" data-dp="tenant-name">${realmName}</div>
        </td></tr>
        <tr><td style="padding:8px 32px 24px;font-size:15px;line-height:1.55;">
          <#nested>
        </td></tr>
        <tr><td style="padding:16px 32px;background:#f8f8ff;font-size:12px;color:#5b5b78;">
          Secured by Dovepeak Identity. This is an automated message; replies are not monitored.
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
</#macro>

<#-- A call-to-action button in the tenant colour. -->
<#macro button href label>
<#assign dpPrimary = msg("dovepeakBrandPrimaryColor")?matches("#[0-9A-Fa-f]{6}")?then(msg("dovepeakBrandPrimaryColor"), "#ff6300")>
<#assign dpOnPrimary = msg("dovepeakBrandOnPrimaryColor")?matches("#[0-9A-Fa-f]{6}")?then(msg("dovepeakBrandOnPrimaryColor"), "#ffffff")>
<p style="margin:24px 0;"><a href="${href}" data-dp="action-link" style="background:${dpPrimary};color:${dpOnPrimary};text-decoration:none;padding:12px 22px;border-radius:8px;font-weight:600;display:inline-block;">${label}</a></p>
<p style="font-size:12px;color:#5b5b78;word-break:break-all;">${msg("dovepeakLinkFallback")} ${href}</p>
</#macro>
